// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace FSharp.Compiler.EditorServices

open System.Diagnostics
open System.Collections.Generic
open System.Collections.Immutable
open Internal.Utilities.Library
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Import
open FSharp.Compiler.Infos
open FSharp.Compiler.DiagnosticsLogger
open FSharp.Compiler.NameResolution
open FSharp.Compiler.Syntax.PrettyNaming
open FSharp.Compiler.TcGlobals
open FSharp.Compiler.Text
open FSharp.Compiler.Text.Range
open FSharp.Compiler.TypedTree
open FSharp.Compiler.TypedTreeOps
open FSharp.Compiler.TypeHierarchy

type SemanticClassificationType =
    | ReferenceType = 0
    | ValueType = 1
    | UnionCase = 2
    | UnionCaseField = 3
    | Function = 4
    | Property = 5
    | MutableVar = 6
    | Module = 7
    | Namespace = 8
    | Printf = 9
    | ComputationExpression = 10
    | IntrinsicFunction = 11
    | Enumeration = 12
    | Interface = 13
    | TypeArgument = 14
    | Operator = 15
    | DisposableType = 16
    | DisposableTopLevelValue = 17
    | DisposableLocalValue = 18
    | Method = 19
    | ExtensionMethod = 20
    | ConstructorForReferenceType = 21
    | ConstructorForValueType = 22
    | Literal = 23
    | RecordField = 24
    | MutableRecordField = 25
    | RecordFieldAsFunction = 26
    | Exception = 27
    | Field = 28
    | Event = 29
    | Delegate = 30
    | NamedArgument = 31
    | Value = 32
    | LocalValue = 33
    | Type = 34
    | TypeDef = 35
    | Plaintext = 36

[<Struct>]
type SemanticClassificationItem =
    val Range: range
    val Type: SemanticClassificationType
    new((range, ty)) = { Range = range; Type = ty }

[<AutoOpen>]
module TcResolutionsExtensions =
    let (|CNR|) (cnr: CapturedNameResolution) =
        (cnr.Item, cnr.ItemOccurrence, cnr.DisplayEnv, cnr.NameResolutionEnv, cnr.AccessorDomain, cnr.Range)

    let isDisposableTy g amap (ty: TType) =
        not (typeEquiv g ty g.system_IDisposable_ty)
        && protectAssemblyExplorationNoReraise false false (fun () ->
            ExistsHeadTypeInEntireHierarchy g amap range0 ty g.tcref_System_IDisposable)

    let isDiscard (str: string) = str.StartsWithOrdinal("_")

    let isValRefDisposable g amap (vref: ValRef) =
        not (isDiscard vref.DisplayName)
        &&
        // For values, we actually do want to color things if they literally are IDisposables
        protectAssemblyExplorationNoReraise false false (fun () ->
            ExistsHeadTypeInEntireHierarchy g amap range0 vref.Type g.tcref_System_IDisposable)

    let isStructTyconRef g (tcref: TyconRef) =
        let ty = generalizedTyconRef g tcref
        let underlyingTy = stripTyEqnsAndMeasureEqns g ty
        isStructTy g underlyingTy

    let isValRefMutable g (vref: ValRef) =
        // Mutable values, ref cells, and non-inref byrefs are mutable.
        vref.IsMutable
        || isRefCellTy g vref.Type
        || (isByrefTy g vref.Type && not (isInByrefTy g vref.Type))

    let isRecdFieldMutable g (rfinfo: RecdFieldInfo) =
        (rfinfo.RecdField.IsMutable && rfinfo.LiteralValue.IsNone)
        || isRefCellTy g rfinfo.RecdField.FormalType

    let reprToClassificationType g repr tcref =
        match repr with
        | TFSharpTyconRepr om ->
            match om.fsobjmodel_kind with
            | TFSharpUnion
            | TFSharpRecord ->
                if isStructTyconRef g tcref then
                    SemanticClassificationType.ValueType
                else
                    SemanticClassificationType.Type
            | TFSharpClass -> SemanticClassificationType.ReferenceType
            | TFSharpInterface -> SemanticClassificationType.Interface
            | TFSharpStruct -> SemanticClassificationType.ValueType
            | TFSharpDelegate _ -> SemanticClassificationType.Delegate
            | TFSharpEnum -> SemanticClassificationType.Enumeration
        | TILObjectRepr(TILObjectReprData(_, _, td)) ->
            if td.IsClass then
                SemanticClassificationType.ReferenceType
            elif td.IsStruct then
                SemanticClassificationType.ValueType
            elif td.IsInterface then
                SemanticClassificationType.Interface
            elif td.IsEnum then
                SemanticClassificationType.Enumeration
            else
                SemanticClassificationType.Delegate
        | TAsmRepr _ -> SemanticClassificationType.TypeDef
        | TMeasureableRepr _ -> SemanticClassificationType.TypeDef
#if !NO_TYPEPROVIDERS
        | TProvidedTypeRepr _ -> SemanticClassificationType.TypeDef
        | TProvidedNamespaceRepr _ -> SemanticClassificationType.TypeDef
#endif
        | TNoRepr -> SemanticClassificationType.ReferenceType

    type TcResolutions with

        member sResolutions.GetSemanticClassification
            (g: TcGlobals, amap: ImportMap, formatSpecifierLocations: (range * int)[], range: range option)
            : SemanticClassificationItem[] =
            DiagnosticsScope.Protect
                range0
                (fun () ->
                    let (|LegitTypeOccurrence|_|) occ =
                        match occ with
                        | ItemOccurrence.UseInType
                        | ItemOccurrence.UseInAttribute
                        | ItemOccurrence.Use
                        | ItemOccurrence.Binding
                        | ItemOccurrence.Pattern
                        | ItemOccurrence.Open -> Some()
                        | _ -> None

                    let (|KeywordIntrinsicValue|_|) (vref: ValRef) =
                        if
                            valRefEq g g.raise_vref vref
                            || valRefEq g g.reraise_vref vref
                            || valRefEq g g.typeof_vref vref
                            || valRefEq g g.typedefof_vref vref
                            || valRefEq g g.sizeof_vref vref
                            || valRefEq g g.nameof_vref vref
                        then
                            Some()
                        else
                            None

                    let (|EnumCaseFieldInfo|_|) (rfinfo: RecdFieldInfo) =
                        match rfinfo.TyconRef.TypeReprInfo with
                        | TFSharpTyconRepr x ->
                            match x.fsobjmodel_kind with
                            | TFSharpEnum -> Some()
                            | _ -> None
                        | _ -> None

                    // Custom builders like 'async { }' are both Item.Value and Item.CustomBuilder.
                    // We should prefer the latter, otherwise they would not get classified as CEs.
                    let takeCustomBuilder (cnrs: CapturedNameResolution[]) =
                        assert (cnrs.Length > 0)

                        if cnrs.Length = 1 then
                            cnrs
                        elif cnrs.Length = 2 then
                            match cnrs[0].Item, cnrs[1].Item with
                            | Item.Value _, Item.CustomBuilder _ -> [| cnrs[1] |]
                            | Item.CustomBuilder _, Item.Value _ -> [| cnrs[0] |]
                            | _ -> cnrs
                        else
                            cnrs

                    let resolutions =
                        match range with
                        | Some range ->
                            sResolutions.CapturedNameResolutions.ToArray()
                            |> Array.filter (fun cnr -> rangeContainsPos range cnr.Range.Start || rangeContainsPos range cnr.Range.End)
                            |> Array.groupBy (fun cnr -> cnr.Range)
                            |> Array.collect (fun (_, cnrs) -> takeCustomBuilder cnrs)
                        | None -> sResolutions.CapturedNameResolutions.ToArray()

                    let duplicates = HashSet<range>(comparer)

                    let results = ImmutableArray.CreateBuilder()

                    let inline add m (typ: SemanticClassificationType) =
                        if duplicates.Add m then
                            results.Add(SemanticClassificationItem((m, typ)))

                    resolutions
                    |> Array.iter (fun cnr ->
                        match cnr.Item, cnr.ItemOccurrence, cnr.Range with
                        | (Item.CustomBuilder _ | Item.CustomOperation _), ItemOccurrence.Use, m ->
                            add m SemanticClassificationType.ComputationExpression

                        | Item.Value vref, _, m when isValRefMutable g vref -> add m SemanticClassificationType.MutableVar

                        | Item.Value KeywordIntrinsicValue, ItemOccurrence.Use, m -> add m SemanticClassificationType.IntrinsicFunction

                        | Item.Value vref, _, m when isForallFunctionTy g vref.Type ->
                            if isDiscard vref.DisplayName then
                                add m SemanticClassificationType.Plaintext
                            elif valRefEq g g.range_op_vref vref || valRefEq g g.range_step_op_vref vref then
                                add m SemanticClassificationType.Operator
                            elif vref.IsPropertyGetterMethod || vref.IsPropertySetterMethod then
                                add m SemanticClassificationType.Property
                            elif vref.IsMember then
                                add m SemanticClassificationType.Method
                            elif IsOperatorDisplayName vref.DisplayName then
                                add m SemanticClassificationType.Operator
                            else
                                add m SemanticClassificationType.Function

                        | Item.Value vref, _, m ->
                            if isValRefDisposable g amap vref then
                                if vref.IsCompiledAsTopLevel then
                                    add m SemanticClassificationType.DisposableTopLevelValue
                                else
                                    add m SemanticClassificationType.DisposableLocalValue
                            elif Option.isSome vref.LiteralValue then
                                add m SemanticClassificationType.Literal
                            elif not vref.IsCompiledAsTopLevel && not (isDiscard vref.DisplayName) then
                                add m SemanticClassificationType.LocalValue
                            else
                                add m SemanticClassificationType.Value

                        | Item.RecdField rfinfo, _, m ->
                            match rfinfo with
                            | EnumCaseFieldInfo -> add m SemanticClassificationType.Enumeration
                            | _ ->
                                if isRecdFieldMutable g rfinfo then
                                    add m SemanticClassificationType.MutableRecordField
                                elif isFunTy g rfinfo.FieldType then
                                    add m SemanticClassificationType.RecordFieldAsFunction
                                else
                                    add m SemanticClassificationType.RecordField

                        | Item.AnonRecdField(_, tys, idx, m), _, _ ->
                            let ty = tys[idx]

                            // It's not currently possible for anon record fields to be mutable, but they can be ref cells
                            if isRefCellTy g ty then
                                add m SemanticClassificationType.MutableRecordField
                            elif isFunTy g ty then
                                add m SemanticClassificationType.RecordFieldAsFunction
                            else
                                add m SemanticClassificationType.RecordField

                        | Item.Property(info = pinfo :: _), _, m ->
                            if not pinfo.IsIndexer then
                                add m SemanticClassificationType.Property

                        | Item.CtorGroup(_, minfos), _, m ->
                            match minfos with
                            | [] -> add m SemanticClassificationType.ConstructorForReferenceType
                            | _ ->
                                if
                                    minfos
                                    |> List.forall (fun minfo -> isDisposableTy g amap minfo.ApparentEnclosingType)
                                then
                                    add m SemanticClassificationType.DisposableType
                                elif minfos |> List.forall (fun minfo -> isStructTy g minfo.ApparentEnclosingType) then
                                    add m SemanticClassificationType.ConstructorForValueType
                                else
                                    add m SemanticClassificationType.ConstructorForReferenceType

                        | Item.DelegateCtor _, _, m -> add m SemanticClassificationType.ConstructorForReferenceType

                        | Item.MethodGroup(_, minfos, _), _, m ->
                            match minfos with
                            | [] -> add m SemanticClassificationType.Method
                            | _ ->
                                if
                                    minfos
                                    |> List.forall (fun minfo -> minfo.IsExtensionMember || minfo.IsCSharpStyleExtensionMember)
                                then
                                    add m SemanticClassificationType.ExtensionMethod
                                else
                                    add m SemanticClassificationType.Method

                        // Special case measures for struct types
                        | Item.Types(_, AppTy g (tyconRef, TType_measure _ :: _) :: _), LegitTypeOccurrence, m when
                            isStructTyconRef g tyconRef
                            ->
                            add m SemanticClassificationType.ValueType

                        | Item.Types(_, ty :: _), LegitTypeOccurrence, m ->
                            let ty = stripTyEqns g ty

                            if isDisposableTy g amap ty then
                                add m SemanticClassificationType.DisposableType
                            else
                                match tryTcrefOfAppTy g ty with
                                | ValueSome tcref -> add m (reprToClassificationType g tcref.TypeReprInfo tcref)
                                | ValueNone ->
                                    if isStructTupleTy g ty then
                                        add m SemanticClassificationType.ValueType
                                    elif isRefTupleTy g ty then
                                        add m SemanticClassificationType.ReferenceType
                                    elif isForallFunctionTy g ty then
                                        add m SemanticClassificationType.Function
                                    elif isTyparTy g ty then
                                        add m SemanticClassificationType.ValueType
                                    else
                                        add m SemanticClassificationType.TypeDef

                        | Item.TypeVar _, LegitTypeOccurrence, m -> add m SemanticClassificationType.TypeArgument

                        | Item.ExnCase _, LegitTypeOccurrence, m -> add m SemanticClassificationType.Exception

                        | Item.ModuleOrNamespaces(modref :: _), LegitTypeOccurrence, m ->
                            if modref.IsNamespace then
                                add m SemanticClassificationType.Namespace
                            else
                                add m SemanticClassificationType.Module

                        | Item.ActivePatternCase _, _, m -> add m SemanticClassificationType.UnionCase

                        | Item.UnionCase _, _, m -> add m SemanticClassificationType.UnionCase

                        | Item.Trait _, _, m -> add m SemanticClassificationType.Method

                        | Item.ActivePatternResult _, _, m -> add m SemanticClassificationType.UnionCase

                        | Item.UnionCaseField _, _, m -> add m SemanticClassificationType.UnionCaseField

                        | Item.ILField _, _, m -> add m SemanticClassificationType.Field

                        | Item.Event _, _, m -> add m SemanticClassificationType.Event

                        | Item.OtherName _, _, m -> add m SemanticClassificationType.NamedArgument

                        | Item.SetterArg _, _, m -> add m SemanticClassificationType.NamedArgument

                        | Item.UnqualifiedType(tcref :: _), LegitTypeOccurrence, m ->
                            if tcref.IsEnumTycon || tcref.IsILEnumTycon then
                                add m SemanticClassificationType.Enumeration
                            elif tcref.IsFSharpException then
                                add m SemanticClassificationType.Exception
                            elif tcref.IsFSharpDelegateTycon then
                                add m SemanticClassificationType.Delegate
                            elif tcref.IsFSharpInterfaceTycon then
                                add m SemanticClassificationType.Interface
                            elif tcref.IsFSharpStructOrEnumTycon then
                                add m SemanticClassificationType.ValueType
                            elif tcref.IsModule then
                                add m SemanticClassificationType.Module
                            elif tcref.IsNamespace then
                                add m SemanticClassificationType.Namespace
                            elif tcref.IsUnionTycon || tcref.IsRecordTycon then
                                if isStructTyconRef g tcref then
                                    add m SemanticClassificationType.ValueType
                                else
                                    add m SemanticClassificationType.UnionCase
                            elif tcref.IsILTycon then
                                let (TILObjectReprData(_, _, tydef)) = tcref.ILTyconInfo

                                if tydef.IsInterface then
                                    add m SemanticClassificationType.Interface
                                elif tydef.IsDelegate then
                                    add m SemanticClassificationType.Delegate
                                elif tydef.IsEnum then
                                    add m SemanticClassificationType.Enumeration
                                elif tydef.IsStruct then
                                    add m SemanticClassificationType.ValueType
                                else
                                    add m SemanticClassificationType.ReferenceType

                        | _, _, m -> add m SemanticClassificationType.Plaintext)

                    let locs =
                        formatSpecifierLocations
                        |> Array.map (fun (m, _) -> SemanticClassificationItem((m, SemanticClassificationType.Printf)))

                    results.AddRange(locs)
                    results.ToArray())
                (fun msg ->
                    Trace.TraceInformation(sprintf "FCS: recovering from error in GetSemanticClassification: '%s'" msg)
                    Array.empty)
