// Copyright (c) Microsoft Corporation. All Rights Reserved. See License.txt in the project root for license information.
  
//-------------------------------------------------------------------------
// Defines the typed abstract syntax trees used throughout the F# compiler.
//------------------------------------------------------------------------- 

module internal FSharp.Compiler.TypedTreeBasics

open Internal.Utilities.Library
open FSharp.Compiler.AbstractIL.IL 
open FSharp.Compiler.CompilerGlobalState
open FSharp.Compiler.Text
open FSharp.Compiler.Syntax
open FSharp.Compiler.TypedTree

#if DEBUG
assert (sizeof<ValFlags> = 8)
assert (sizeof<EntityFlags> = 8)
assert (sizeof<TyparFlags> = 4)
#endif

/// Metadata on values (names of arguments etc.) 
module ValReprInfo = 

    let unnamedTopArg1: ArgReprInfo = { Attribs = []; Name = None; OtherRange = None }

    let unnamedTopArg = [unnamedTopArg1]

    let unitArgData: ArgReprInfo list list = [[]]

    let unnamedRetVal: ArgReprInfo = { Attribs = []; Name = None; OtherRange = None }

    let selfMetadata = unnamedTopArg

    let emptyValData = ValReprInfo([], [], unnamedRetVal)

    let IsEmpty info =
        match info with
        | ValReprInfo([], [], { Attribs = []; Name = None; OtherRange = None }) -> true
        | _ -> false

    let InferTyparInfo (tps: Typar list) = tps |> List.map (fun tp -> TyparReprInfo(tp.Id, tp.Kind))

    let InferArgReprInfo (v: Val) : ArgReprInfo = { Attribs = []; Name = Some v.Id; OtherRange = None }

    let InferArgReprInfos (vs: Val list list) = ValReprInfo([], List.mapSquared InferArgReprInfo vs, unnamedRetVal)

    let HasNoArgs (ValReprInfo(n, args, _)) = n.IsEmpty && args.IsEmpty

//---------------------------------------------------------------------------
// Basic properties via functions (old style)
//---------------------------------------------------------------------------

let typeOfVal (v: Val) = v.Type

let typesOfVals (v: Val list) = v |> List.map (fun v -> v.Type)

let nameOfVal (v: Val) = v.LogicalName

let arityOfVal (v: Val) =
    match v.ValReprInfo with
    | None -> ValReprInfo.emptyValData
    | Some info -> info

let tryGetArityOfValForDisplay (v: Val) =
    v.ValReprInfoForDisplay
    |> Option.orElseWith (fun _ -> v.ValReprInfo)

let arityOfValForDisplay (v: Val) =
    tryGetArityOfValForDisplay v |> Option.defaultValue ValReprInfo.emptyValData

let tupInfoRef = TupInfo.Const false

let tupInfoStruct = TupInfo.Const true

let mkTupInfo b = if b then tupInfoStruct else tupInfoRef

let structnessDefault = false

let mkRawRefTupleTy tys = TType_tuple (tupInfoRef, tys)

let mkRawStructTupleTy tys = TType_tuple (tupInfoStruct, tys)

//---------------------------------------------------------------------------
// Equality relations on locally defined things 
//---------------------------------------------------------------------------

let typarEq (tp1: Typar) (tp2: Typar) = (tp1.Stamp = tp2.Stamp)

/// Equality on type variables, implemented as reference equality. This should be equivalent to using typarEq.
let typarRefEq (tp1: Typar) (tp2: Typar) = (tp1 === tp2)

/// Equality on value specs, implemented as reference equality
let valEq (v1: Val) (v2: Val) = (v1 === v2)

/// Equality on CCU references, implemented as reference equality except when unresolved
let ccuEq (ccu1: CcuThunk) (ccu2: CcuThunk) = 
    (ccu1 === ccu2) || 
    (if ccu1.IsUnresolvedReference || ccu2.IsUnresolvedReference then 
        ccu1.AssemblyName = ccu2.AssemblyName
     else 
        ccu1.Contents === ccu2.Contents)

/// For dereferencing in the middle of a pattern
let (|ValDeref|) (vref: ValRef) = vref.Deref

//--------------------------------------------------------------------------
// Make references to TAST items
//--------------------------------------------------------------------------

let mkRecdFieldRef tcref f = RecdFieldRef(tcref, f)

let mkUnionCaseRef tcref c = UnionCaseRef(tcref, c)

let ERefLocal x: EntityRef = { binding=x; nlr=Unchecked.defaultof<_> }      

let ERefNonLocal x: EntityRef = { binding=Unchecked.defaultof<_>; nlr=x }      

let ERefNonLocalPreResolved x xref: EntityRef = { binding=x; nlr=xref }      

let (|ERefLocal|ERefNonLocal|) (x: EntityRef) = 
    match box x.nlr with 
    | null -> ERefLocal x.binding
    | _ -> ERefNonLocal x.nlr

//--------------------------------------------------------------------------
// Construct local references
//-------------------------------------------------------------------------- 

let mkLocalTyconRef x = ERefLocal x

let mkNonLocalEntityRef ccu mp = NonLocalEntityRef(ccu, mp)

let mkNestedNonLocalEntityRef (nleref: NonLocalEntityRef) id =
    mkNonLocalEntityRef nleref.Ccu (Array.append nleref.Path [| id |])

let mkNonLocalTyconRef nleref id = ERefNonLocal (mkNestedNonLocalEntityRef nleref id)

let mkNonLocalTyconRefPreResolved x nleref id =
    ERefNonLocalPreResolved x (mkNestedNonLocalEntityRef nleref id)

type EntityRef with 

    member tcref.NestedTyconRef (x: Entity) = 
        match tcref with 
        | ERefLocal _ -> mkLocalTyconRef x
        | ERefNonLocal nlr -> mkNonLocalTyconRefPreResolved x nlr x.LogicalName

    member tcref.RecdFieldRefInNestedTycon tycon (id: Ident) = RecdFieldRef (tcref.NestedTyconRef tycon, id.idText)

/// Make a reference to a union case for type in a module or namespace
let mkModuleUnionCaseRef (modref: ModuleOrNamespaceRef) tycon uc = 
    (modref.NestedTyconRef tycon).MakeNestedUnionCaseRef uc

let VRefLocal x: ValRef = { binding=x; nlr=Unchecked.defaultof<_> }      

let VRefNonLocal x: ValRef = { binding=Unchecked.defaultof<_>; nlr=x }      

let VRefNonLocalPreResolved x xref: ValRef = { binding=x; nlr=xref }      

let (|VRefLocal|VRefNonLocal|) (x: ValRef) = 
    match box x.nlr with 
    | null -> VRefLocal x.binding
    | _ -> VRefNonLocal x.nlr

let mkNonLocalValRef mp id = VRefNonLocal {EnclosingEntity = ERefNonLocal mp; ItemKey=id }

let mkNonLocalValRefPreResolved x mp id = VRefNonLocalPreResolved x {EnclosingEntity = ERefNonLocal mp; ItemKey=id }

let ccuOfValRef vref =  
    match vref with 
    | VRefLocal _ -> None
    | VRefNonLocal nlr -> Some nlr.Ccu

let ccuOfTyconRef eref =  
    match eref with 
    | ERefLocal _ -> None
    | ERefNonLocal nlr -> Some nlr.Ccu

//--------------------------------------------------------------------------
// Type parameters and inference unknowns
//-------------------------------------------------------------------------

let NewNullnessVar() = Nullness.Variable (NullnessVar()) // we don't known (and if we never find out then it's non-null)

let KnownAmbivalentToNull = Nullness.Known NullnessInfo.AmbivalentToNull

let KnownWithNull = Nullness.Known NullnessInfo.WithNull

let KnownWithoutNull = Nullness.Known NullnessInfo.WithoutNull

let mkTyparTy (tp:Typar) = 
    match tp.Kind with 
    | TyparKind.Type -> tp.AsType KnownWithoutNull
    | TyparKind.Measure -> TType_measure (Measure.Var tp)

// For fresh type variables clear the StaticReq when copying because the requirement will be re-established through the
// process of type inference.
let copyTypar clearStaticReq (tp: Typar) = 
    let optData = tp.typar_opt_data |> Option.map (fun tg -> { typar_il_name = tg.typar_il_name; typar_xmldoc = tg.typar_xmldoc; typar_constraints = tg.typar_constraints; typar_attribs = tg.typar_attribs; typar_is_contravariant = tg.typar_is_contravariant  })
    let flags = if clearStaticReq then tp.typar_flags.WithStaticReq(TyparStaticReq.None) else tp.typar_flags
    Typar.New { typar_id = tp.typar_id
                typar_flags = flags
                typar_stamp = newStamp()
                typar_solution = tp.typar_solution
                typar_astype = Unchecked.defaultof<_>
                // Be careful to clone the mutable optional data too
                typar_opt_data = optData } 

let copyTypars clearStaticReq tps = List.map (copyTypar clearStaticReq) tps

//--------------------------------------------------------------------------
// Inference variables
//-------------------------------------------------------------------------- 
    
let tryShortcutSolvedUnitPar canShortcut (r: Typar) = 
    if r.Kind = TyparKind.Type then failwith "tryShortcutSolvedUnitPar: kind=type"
    match r.Solution with
    | Some (TType_measure unt) -> 
        if canShortcut then 
            match unt with 
            | Measure.Var r2 -> 
               match r2.Solution with
               | None -> ()
               | Some _ as soln -> 
                  r.typar_solution <- soln
            | _ -> () 
        unt
    | _ -> 
        failwith "tryShortcutSolvedUnitPar: unsolved"
      
let rec stripUnitEqnsAux canShortcut unt = 
    match unt with 
    | Measure.Var r when r.IsSolved -> stripUnitEqnsAux canShortcut (tryShortcutSolvedUnitPar canShortcut r)
    | _ -> unt

let combineNullness (nullnessOrig: Nullness) (nullnessNew: Nullness) = 
    match nullnessOrig, nullnessNew with
    | Nullness.Variable _, Nullness.Known NullnessInfo.WithoutNull -> 
        nullnessOrig
    | _ -> 
        match nullnessOrig.Evaluate() with
        | NullnessInfo.WithoutNull -> nullnessNew
        | NullnessInfo.AmbivalentToNull ->
            match nullnessNew.Evaluate() with
            | NullnessInfo.WithoutNull -> nullnessOrig
            | NullnessInfo.AmbivalentToNull -> nullnessOrig
            | NullnessInfo.WithNull -> nullnessNew
        | NullnessInfo.WithNull -> 
            match nullnessNew.Evaluate() with
            | NullnessInfo.WithoutNull -> nullnessOrig
            | NullnessInfo.AmbivalentToNull -> nullnessNew
            | NullnessInfo.WithNull -> nullnessOrig

let nullnessEquiv (nullnessOrig: Nullness) (nullnessNew: Nullness) = LanguagePrimitives.PhysicalEquality nullnessOrig nullnessNew

let tryAddNullnessToTy nullnessNew (ty:TType) = 
    match ty with
    | TType_var (tp, nullnessOrig) -> 
        let nullnessAfter = combineNullness nullnessOrig nullnessNew
        if nullnessEquiv nullnessAfter nullnessOrig then
            Some ty
        else 
            Some (TType_var (tp, nullnessAfter))
    | TType_app (tcr, tinst, nullnessOrig) -> 
        let nullnessAfter = combineNullness nullnessOrig nullnessNew
        if nullnessEquiv nullnessAfter nullnessOrig then
            Some ty
        else 
            Some (TType_app (tcr, tinst, nullnessAfter))
    | TType_ucase _ -> None
    | TType_tuple _ -> None
    | TType_anon _ -> None
    | TType_fun (d, r, nullnessOrig) ->
        let nullnessAfter = combineNullness nullnessOrig nullnessNew
        if nullnessEquiv nullnessAfter nullnessOrig then
            Some ty
        else 
            Some (TType_fun (d, r, nullnessAfter))
    | TType_forall _ -> None
    | TType_measure _ -> None

let addNullnessToTy (nullness: Nullness) (ty:TType) =
    match nullness with
    | Nullness.Known NullnessInfo.WithoutNull -> ty
    | Nullness.Variable nv when nv.IsFullySolved && nv.TryEvaluate() = ValueSome NullnessInfo.WithoutNull -> ty
    | _ -> 
    match ty with
    | TType_var (tp, nullnessOrig) -> TType_var (tp, combineNullness nullnessOrig nullness)
    | TType_app (tcr, tinst, nullnessOrig) -> 
        let tycon = tcr.Deref
        if tycon.IsStructRecordOrUnionTycon || tycon.IsStructOrEnumTycon then
            ty
        else 
            TType_app (tcr, tinst, combineNullness nullnessOrig nullness)
    | TType_fun (d, r, nullnessOrig) -> TType_fun (d, r, combineNullness nullnessOrig nullness)
    | _ -> ty

let rec stripTyparEqnsAux nullness0 canShortcut ty = 
    match ty with 
    | TType_var (r, nullness) -> 
        match r.Solution with
        | Some soln -> 
            if canShortcut then 
                match soln with 
                // We avoid shortcutting when there are additional constraints on the type variable we're trying to cut out
                // This is only because IterType likes to walk _all_ the constraints _everywhere_ in a type, including
                // those attached to _solved_ type variables. In an ideal world this would never be needed - see the notes
                // on IterType.
                | TType_var (r2, nullness2) when r2.Constraints.IsEmpty -> 
                   match nullness2.Evaluate() with 
                   | NullnessInfo.WithoutNull -> 
                       match r2.Solution with
                       | None -> ()
                       | Some _ as soln2 -> 
                          r.typar_solution <- soln2
                   | _ -> ()
                | _ -> () 
            stripTyparEqnsAux (combineNullness nullness0 nullness) canShortcut soln
        | None -> 
            addNullnessToTy nullness0 ty
    | TType_measure unt -> 
        TType_measure (stripUnitEqnsAux canShortcut unt)
    | _ -> addNullnessToTy nullness0 ty

let stripTyparEqns ty = stripTyparEqnsAux KnownWithoutNull false ty

let stripUnitEqns unt = stripUnitEqnsAux false unt

let replaceNullnessOfTy nullness (ty:TType) =
    match stripTyparEqns ty with
    | TType_var (tp, _) -> TType_var (tp, nullness)
    | TType_app (tcr, tinst, _) -> TType_app (tcr, tinst, nullness)
    | TType_fun (d, r, _) -> TType_fun (d, r, nullness)
    | sty -> sty

/// Detect a use of a nominal type, including type abbreviations.
[<return: Struct>]
let (|AbbrevOrAppTy|_|) (ty: TType) =
    match stripTyparEqns ty with
    | TType_app (tcref, tinst, _) -> ValueSome(tcref, tinst)
    | _ -> ValueNone

//---------------------------------------------------------------------------
// These make local/non-local references to values according to whether
// the item is globally stable ("published") or not.
//---------------------------------------------------------------------------

let mkLocalValRef (v: Val) = VRefLocal v
let mkLocalModuleRef (v: ModuleOrNamespace) = ERefLocal v
let mkLocalEntityRef (v: Entity) = ERefLocal v

let mkNonLocalCcuRootEntityRef ccu (x: Entity) = mkNonLocalTyconRefPreResolved x (mkNonLocalEntityRef ccu [| |]) x.LogicalName

let mkNestedValRef (cref: EntityRef) (v: Val) : ValRef = 
    match cref with 
    | ERefLocal _ -> mkLocalValRef v
    | ERefNonLocal nlr -> 
        let key = v.GetLinkageFullKey()
        mkNonLocalValRefPreResolved v nlr key

/// From Ref_private to Ref_nonlocal when exporting data.
let rescopePubPathToParent viewedCcu (PubPath p) = NonLocalEntityRef(viewedCcu, p[0..p.Length-2])

/// From Ref_private to Ref_nonlocal when exporting data.
let rescopePubPath viewedCcu (PubPath p) = NonLocalEntityRef(viewedCcu, p)

//---------------------------------------------------------------------------
// Equality between TAST items.
//---------------------------------------------------------------------------

let valRefInThisAssembly compilingFSharpCore (x: ValRef) = 
    match x with 
    | VRefLocal _ -> true
    | VRefNonLocal _ -> compilingFSharpCore

let tyconRefUsesLocalXmlDoc compilingFSharpCore (x: TyconRef) = 
    match x with 
    | ERefLocal _ -> true
    | ERefNonLocal _ ->
#if !NO_TYPEPROVIDERS
        match x.TypeReprInfo with
        | TProvidedTypeRepr _ -> true
        | _ -> 
#endif
        compilingFSharpCore
    
let entityRefInThisAssembly compilingFSharpCore (x: EntityRef) = 
    match x with 
    | ERefLocal _ -> true
    | ERefNonLocal _ -> compilingFSharpCore

let arrayPathEq (y1: string[]) (y2: string[]) =
    let len1 = y1.Length 
    let len2 = y2.Length 
    (len1 = len2) && 
    (let rec loop i = (i >= len1) || (y1[i] = y2[i] && loop (i+1)) 
     loop 0)

let nonLocalRefEq (NonLocalEntityRef(x1, y1) as smr1) (NonLocalEntityRef(x2, y2) as smr2) = 
    smr1 === smr2 || (ccuEq x1 x2 && arrayPathEq y1 y2)

/// This predicate tests if non-local resolution paths are definitely known to resolve
/// to different entities. All references with different named paths always resolve to 
/// different entities. Two references with the same named paths may resolve to the same 
/// entities even if they reference through different CCUs, because one reference
/// may be forwarded to another via a .NET TypeForwarder.
let nonLocalRefDefinitelyNotEq (NonLocalEntityRef(_, y1)) (NonLocalEntityRef(_, y2)) = 
    not (arrayPathEq y1 y2)

let pubPathEq (PubPath path1) (PubPath path2) = arrayPathEq path1 path2

let fslibRefEq (nlr1: NonLocalEntityRef) (PubPath path2) =
    arrayPathEq nlr1.Path path2

// Compare two EntityRef's for equality when compiling fslib (FSharp.Core.dll)
//
// Compiler-internal references to items in fslib are Ref_nonlocals even when compiling fslib.
// This breaks certain invariants that hold elsewhere, because they dereference to point to 
// Entity's from signatures rather than Entity's from implementations. This means backup, alternative 
// equality comparison techniques are needed when compiling fslib itself.
let fslibEntityRefEq fslibCcu (eref1: EntityRef) (eref2: EntityRef) =
    match eref1, eref2 with 
    | ERefNonLocal nlr1, ERefLocal x2
    | ERefLocal x2, ERefNonLocal nlr1 ->
        ccuEq nlr1.Ccu fslibCcu &&
        match x2.PublicPath with 
        | Some pp2 -> fslibRefEq nlr1 pp2
        | None -> false
    | ERefLocal e1, ERefLocal e2 ->
        match e1.PublicPath, e2.PublicPath with 
        | Some pp1, Some pp2 -> pubPathEq pp1 pp2
        | _ -> false
    | _ -> false

// Compare two ValRef's for equality when compiling fslib (FSharp.Core.dll)
//
// Compiler-internal references to items in fslib are Ref_nonlocals even when compiling fslib.
// This breaks certain invariants that hold elsewhere, because they dereference to point to 
// Val's from signatures rather than Val's from implementations. This means backup, alternative 
// equality comparison techniques are needed when compiling fslib itself.
let fslibValRefEq fslibCcu vref1 vref2 =
    match vref1, vref2 with 
    | VRefNonLocal nlr1, VRefLocal x2
    | VRefLocal x2, VRefNonLocal nlr1 ->
        ccuEq nlr1.Ccu fslibCcu &&
        match x2.PublicPath with 
        | Some (ValPubPath(pp2, nm2)) -> 
            // Note: this next line is just comparing the values by name, and not even the partial linkage data
            // This relies on the fact that the compiler doesn't use any references to
            // entities in fslib that are overloaded, or, if they are overloaded, then value identity
            // is not significant
            nlr1.ItemKey.PartialKey = nm2.PartialKey &&
            fslibRefEq nlr1.EnclosingEntity.nlr pp2
        | _ -> 
            false
    // Note: I suspect this private-to-private reference comparison is not needed
    | VRefLocal e1, VRefLocal e2 ->
        match e1.PublicPath, e2.PublicPath with 
        | Some (ValPubPath(pp1, nm1)), Some (ValPubPath(pp2, nm2)) -> 
            pubPathEq pp1 pp2 && 
            (nm1 = nm2)
        | _ -> false
    | _ -> false
  
/// Primitive routine to compare two EntityRef's for equality
/// This takes into account the possibility that they may have type forwarders
let primEntityRefEq compilingFSharpCore fslibCcu (x: EntityRef) (y: EntityRef) = 
    x === y ||
    
    if x.IsResolved && y.IsResolved && not compilingFSharpCore then
        x.ResolvedTarget === y.ResolvedTarget 
    elif not x.IsLocalRef && not y.IsLocalRef &&
        (// Two tcrefs with identical paths are always equal
         nonLocalRefEq x.nlr y.nlr || 
         // The tcrefs may have forwarders. If they may possibly be equal then resolve them to get their canonical references
         // and compare those using pointer equality.
         (not (nonLocalRefDefinitelyNotEq x.nlr y.nlr) && 
            match x.TryDeref with
            | ValueSome v1 -> match y.TryDeref with ValueSome v2 -> v1 === v2 | _ -> false
            | _ -> match y.TryDeref with ValueNone -> true | _ -> false)) then
        true
    else
        compilingFSharpCore && fslibEntityRefEq fslibCcu x y  

/// Primitive routine to compare two UnionCaseRef's for equality
let primUnionCaseRefEq compilingFSharpCore fslibCcu (UnionCaseRef(tcr1, c1) as uc1) (UnionCaseRef(tcr2, c2) as uc2) = 
    uc1 === uc2 || (primEntityRefEq compilingFSharpCore fslibCcu tcr1 tcr2 && c1 = c2)

/// Primitive routine to compare two ValRef's for equality. On the whole value identity is not particularly
/// significant in F#. However it is significant for
///    (a) Active Patterns 
///    (b) detecting uses of "special known values" from FSharp.Core.dll, such as 'seq' 
///        and quotation splicing 
///
/// Note this routine doesn't take type forwarding into account
let primValRefEq compilingFSharpCore fslibCcu (x: ValRef) (y: ValRef) =
    x === y
    || (x.IsResolved && y.IsResolved && x.ResolvedTarget === y.ResolvedTarget)
    || (x.IsLocalRef && y.IsLocalRef && valEq x.ResolvedTarget y.ResolvedTarget)
    || // Use TryDeref to guard against the platforms/times when certain F# language features aren't available
       match x.TryDeref with
       | ValueSome v1 -> match y.TryDeref with ValueSome v2 -> v1 === v2 | ValueNone -> false
       | ValueNone -> match y.TryDeref with ValueNone -> true | ValueSome _ -> false
    || (compilingFSharpCore && fslibValRefEq fslibCcu x y)

//---------------------------------------------------------------------------
// pubpath/cpath mess
//---------------------------------------------------------------------------

let fullCompPathOfModuleOrNamespace (m: ModuleOrNamespace) = 
    let (CompPath(scoref, sa, cpath)) = m.CompilationPath
    CompPath(scoref, sa, cpath@[(m.LogicalName, m.ModuleOrNamespaceType.ModuleOrNamespaceKind)])

// Can cpath2 be accessed given a right to access cpath1. That is, is cpath2 a nested type or namespace of cpath1. Note order of arguments.
let inline canAccessCompPathFrom (CompPath(scoref1, _, cpath1)) (CompPath(scoref2, _, cpath2)) =
    let rec loop p1 p2 = 
        match p1, p2 with 
        | (a1, k1) :: rest1, (a2, k2) :: rest2 -> (a1=a2) && (k1=k2) && loop rest1 rest2
        | [], _ -> true 
        | _ -> false // cpath1 is longer
    loop cpath1 cpath2 &&
    (scoref1 = scoref2)

let canAccessFromOneOf cpaths cpathTest =
    cpaths |> List.exists (fun cpath -> canAccessCompPathFrom cpath cpathTest) 

let canAccessFrom (TAccess x) cpath = 
    x |> List.forall (fun cpath1 -> canAccessCompPathFrom cpath1 cpath)

let canAccessFromEverywhere (TAccess x) = x.IsEmpty
let canAccessFromSomewhere (TAccess _) = true
let isLessAccessible (TAccess aa) (TAccess bb) = 
    not (aa |> List.forall(fun a -> bb |> List.exists (fun b -> canAccessCompPathFrom a b)))

/// Given (newPath, oldPath) replace oldPath by newPath in the TAccess.
let accessSubstPaths (newPath, oldPath) (TAccess paths) =
    let subst cpath = if cpath=oldPath then newPath else cpath
    TAccess (List.map subst paths)

let compPathOfCcu (ccu: CcuThunk) = CompPath(ccu.ILScopeRef, SyntaxAccess.Unknown, []) 
let taccessPublic = TAccess []
let compPathInternal = CompPath(ILScopeRef.Local, SyntaxAccess.Internal, [])
let taccessInternal = TAccess [compPathInternal]
let taccessPrivate accessPath = let (CompPath(sc,_, paths)) = accessPath in TAccess [CompPath(sc, TypedTree.SyntaxAccess.Private, paths)]

let combineAccess access1 access2 =
    let (TAccess a1) = access1
    let (TAccess a2) = access2
    let combined =
        if access1 = taccessPublic then updateSyntaxAccessForCompPath (a1@a2) TypedTree.SyntaxAccess.Public
        elif access1 = taccessInternal then updateSyntaxAccessForCompPath (a1@a2) TypedTree.SyntaxAccess.Internal
        else (a1@a2)
    TAccess combined

exception Duplicate of string * string * range
exception NameClash of string * string * string * range * string * string * range

