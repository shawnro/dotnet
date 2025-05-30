// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Internal.Text;
using Internal.TypeSystem;
using ILCompiler.DependencyAnalysisFramework;

namespace ILCompiler.DependencyAnalysis
{
    /// Part of Node factory that deals with nodes describing native layout information
    public partial class NodeFactory
    {
        /// <summary>
        /// Helper class that provides a level of grouping for all the native layout lookups
        /// </summary>
        public class NativeLayoutHelper
        {
            private NodeFactory _factory;

            public NativeLayoutHelper(NodeFactory factory)
            {
                _factory = factory;
                CreateNodeCaches();
            }

            private void CreateNodeCaches()
            {
                _typeSignatures = new NodeCache<TypeDesc, NativeLayoutTypeSignatureVertexNode>(type =>
                {
                    return NativeLayoutTypeSignatureVertexNode.NewTypeSignatureVertexNode(_factory, type);
                });

                _methodSignatures = new NodeCache<MethodSignature, NativeLayoutMethodSignatureVertexNode>(signature =>
                {
                    return new NativeLayoutMethodSignatureVertexNode(_factory, signature);
                });

                _placedSignatures = new NodeCache<NativeLayoutVertexNode, NativeLayoutPlacedSignatureVertexNode>(vertexNode =>
                {
                    return new NativeLayoutPlacedSignatureVertexNode(vertexNode);
                });

                _placedVertexSequence = new NodeCache<VertexSequenceKey, NativeLayoutPlacedVertexSequenceVertexNode>(vertices =>
                {
                    return new NativeLayoutPlacedVertexSequenceVertexNode(vertices.Vertices);
                });

                _placedUIntVertexSequence = new NodeCache<List<uint>, NativeLayoutPlacedVertexSequenceOfUIntVertexNode>(uints =>
                {
                    return new NativeLayoutPlacedVertexSequenceOfUIntVertexNode(uints);
                }, new UIntSequenceComparer());

                _methodEntries = new NodeCache<MethodDesc, NativeLayoutMethodEntryVertexNode>(method =>
                {
                    return new NativeLayoutMethodEntryVertexNode(_factory, method, default);
                });

                _templateMethodEntries = new NodeCache<MethodDesc, NativeLayoutTemplateMethodSignatureVertexNode>(method =>
                {
                    return new NativeLayoutTemplateMethodSignatureVertexNode(_factory, method);
                });

                _templateMethodLayouts = new NodeCache<MethodDesc, NativeLayoutTemplateMethodLayoutVertexNode>(method =>
                {
                    return new NativeLayoutTemplateMethodLayoutVertexNode(_factory, method);
                });

                _templateTypeLayouts = new NodeCache<TypeDesc, NativeLayoutTemplateTypeLayoutVertexNode>(type =>
                {
                    return new NativeLayoutTemplateTypeLayoutVertexNode(_factory, type);
                });

                _typeHandle_GenericDictionarySlots = new NodeCache<TypeDesc, NativeLayoutTypeHandleGenericDictionarySlotNode>(type =>
                {
                    return new NativeLayoutTypeHandleGenericDictionarySlotNode(_factory, type);
                });

                _gcStatic_GenericDictionarySlots = new NodeCache<TypeDesc, NativeLayoutGcStaticsGenericDictionarySlotNode>(type =>
                {
                    return new NativeLayoutGcStaticsGenericDictionarySlotNode(_factory, type);
                });

                _nonGcStatic_GenericDictionarySlots = new NodeCache<TypeDesc, NativeLayoutNonGcStaticsGenericDictionarySlotNode>(type =>
                {
                    return new NativeLayoutNonGcStaticsGenericDictionarySlotNode(_factory, type);
                });

                _unwrapNullable_GenericDictionarySlots = new NodeCache<TypeDesc, NativeLayoutUnwrapNullableGenericDictionarySlotNode>(type =>
                {
                    return new NativeLayoutUnwrapNullableGenericDictionarySlotNode(_factory, type);
                });

                _allocateObject_GenericDictionarySlots = new NodeCache<TypeDesc, NativeLayoutAllocateObjectGenericDictionarySlotNode>(type =>
                {
                    return new NativeLayoutAllocateObjectGenericDictionarySlotNode(_factory, type);
                });

                _threadStaticIndex_GenericDictionarySlots = new NodeCache<TypeDesc, NativeLayoutThreadStaticBaseIndexDictionarySlotNode>(type =>
                {
                    return new NativeLayoutThreadStaticBaseIndexDictionarySlotNode(_factory, type);
                });

                _defaultConstructor_GenericDictionarySlots = new NodeCache<TypeDesc, NativeLayoutDefaultConstructorGenericDictionarySlotNode>(type =>
                {
                    return new NativeLayoutDefaultConstructorGenericDictionarySlotNode(_factory, type);
                });

                _interfaceCell_GenericDictionarySlots = new NodeCache<MethodDesc, NativeLayoutInterfaceDispatchGenericDictionarySlotNode>(method =>
               {
                   return new NativeLayoutInterfaceDispatchGenericDictionarySlotNode(_factory, method);
               });

                _methodDictionary_GenericDictionarySlots = new NodeCache<MethodDesc, NativeLayoutMethodDictionaryGenericDictionarySlotNode>(method =>
               {
                   return new NativeLayoutMethodDictionaryGenericDictionarySlotNode(_factory, method);
               });

                _methodEntrypoint_GenericDictionarySlots = new NodeCache<MethodEntrypointSlotKey, NativeLayoutMethodEntrypointGenericDictionarySlotNode>(key =>
               {
                   return new NativeLayoutMethodEntrypointGenericDictionarySlotNode(_factory, key.Method, key.FunctionPointerTarget, key.Unboxing);
               });

                _fieldLdToken_GenericDictionarySlots = new NodeCache<FieldDesc, NativeLayoutFieldLdTokenGenericDictionarySlotNode>(field =>
                {
                    return new NativeLayoutFieldLdTokenGenericDictionarySlotNode(field);
                });

                _methodLdToken_GenericDictionarySlots = new NodeCache<MethodDesc, NativeLayoutMethodLdTokenGenericDictionarySlotNode>(method =>
                {
                    return new NativeLayoutMethodLdTokenGenericDictionarySlotNode(_factory, method);
                });

                _dictionarySignatures = new NodeCache<TypeSystemEntity, NativeLayoutDictionarySignatureNode>(owningMethodOrType =>
                {
                    return new NativeLayoutDictionarySignatureNode(_factory, owningMethodOrType);
                });

                _constrainedMethodUseSlots = new NodeCache<ConstrainedMethodUseKey, NativeLayoutConstrainedMethodDictionarySlotNode>(constrainedMethodUse =>
                {
                    return new NativeLayoutConstrainedMethodDictionarySlotNode(constrainedMethodUse.ConstrainedMethod, constrainedMethodUse.ConstraintType, constrainedMethodUse.DirectCall);
                });
            }

            // Produce a set of dependencies that is necessary such that if this type
            // needs to be used referenced from a NativeLayout template, that the template
            // will be properly constructable.  (This is done by ensuring that all
            // canonical types in the deconstruction of the type are ConstructedEEType instead
            // of just necessary. (Which is what the actual templates signatures will ensure)
            public IEnumerable<IDependencyNode> TemplateConstructableTypes(TypeDesc type)
            {
                // Array types are the only parameterized types that have templates
                if (type.IsSzArray && !type.IsArrayTypeWithoutGenericInterfaces())
                {
                    TypeDesc arrayCanonicalType = type.ConvertToCanonForm(CanonicalFormKind.Specific);

                    // Add a dependency on the template for this type, if the canonical type should be generated into this binary.
                    if (arrayCanonicalType.IsCanonicalSubtype(CanonicalFormKind.Any) && !_factory.NecessaryTypeSymbol(arrayCanonicalType).RepresentsIndirectionCell)
                    {
                        yield return _factory.NativeLayout.TemplateTypeLayout(arrayCanonicalType);
                    }

                    yield return _factory.MaximallyConstructableType(arrayCanonicalType);
                }

                while (type.IsParameterizedType)
                {
                    type = ((ParameterizedType)type).ParameterType;
                }

                if (type.IsFunctionPointer)
                {
                    MethodSignature sig = ((FunctionPointerType)type).Signature;
                    foreach (var dependency in TemplateConstructableTypes(sig.ReturnType))
                        yield return dependency;

                    foreach (var param in sig)
                        foreach (var dependency in TemplateConstructableTypes(param))
                            yield return dependency;

                    // Nothing else to do for function pointers
                    yield break;
                }

                TypeDesc canonicalType = type.ConvertToCanonForm(CanonicalFormKind.Specific);
                yield return _factory.MaximallyConstructableType(canonicalType);

                // Add a dependency on the template for this type, if the canonical type should be generated into this binary.
                if (canonicalType.IsCanonicalSubtype(CanonicalFormKind.Any) && !_factory.NecessaryTypeSymbol(canonicalType).RepresentsIndirectionCell)
                {
                    if (!_factory.TypeSystemContext.IsCanonicalDefinitionType(canonicalType, CanonicalFormKind.Any))
                        yield return _factory.NativeLayout.TemplateTypeLayout(canonicalType);
                }

                foreach (TypeDesc instantiationType in type.Instantiation)
                {
                    foreach (var dependency in TemplateConstructableTypes(instantiationType))
                        yield return dependency;
                }
            }

            private NodeCache<TypeDesc, NativeLayoutTypeSignatureVertexNode> _typeSignatures;
            internal NativeLayoutTypeSignatureVertexNode TypeSignatureVertex(TypeDesc type)
            {
                if (type.IsRuntimeDeterminedType)
                {
                    GenericParameterDesc genericParameter = ((RuntimeDeterminedType)type).RuntimeDeterminedDetailsType;
                    type = _factory.TypeSystemContext.GetSignatureVariable(genericParameter.Index, method: (genericParameter.Kind == GenericParameterKind.Method));
                }

                return _typeSignatures.GetOrAdd(type);
            }

            private NodeCache<MethodSignature, NativeLayoutMethodSignatureVertexNode> _methodSignatures;
            internal NativeLayoutMethodSignatureVertexNode MethodSignatureVertex(MethodSignature signature)
            {
                return _methodSignatures.GetOrAdd(signature);
            }

            private NodeCache<NativeLayoutVertexNode, NativeLayoutPlacedSignatureVertexNode> _placedSignatures;
            internal NativeLayoutPlacedSignatureVertexNode PlacedSignatureVertex(NativeLayoutVertexNode vertexNode)
            {
                return _placedSignatures.GetOrAdd(vertexNode);
            }

            private struct VertexSequenceKey : IEquatable<VertexSequenceKey>
            {
                public readonly List<NativeLayoutVertexNode> Vertices;

                public VertexSequenceKey(List<NativeLayoutVertexNode> vertices)
                {
                    Vertices = vertices;
                }

                public override bool Equals(object obj)
                {
                    VertexSequenceKey? other = obj as VertexSequenceKey?;
                    if (other.HasValue)
                        return Equals(other.Value);
                    else
                        return false;
                }

                public bool Equals(VertexSequenceKey other)
                {
                    if (other.Vertices.Count != Vertices.Count)
                        return false;

                    for (int i = 0; i < Vertices.Count; i++)
                    {
                        if (other.Vertices[i] != Vertices[i])
                            return false;
                    }

                    return true;
                }

                public override int GetHashCode()
                {
                    int hashcode = 0;
                    foreach (NativeLayoutVertexNode node in Vertices)
                    {
                        hashcode ^= node.GetHashCode();
                        hashcode = int.RotateLeft(hashcode, 5);
                    }
                    return hashcode;
                }
            }

            private NodeCache<VertexSequenceKey, NativeLayoutPlacedVertexSequenceVertexNode> _placedVertexSequence;
            internal NativeLayoutPlacedVertexSequenceVertexNode PlacedVertexSequence(List<NativeLayoutVertexNode> vertices)
            {
                return _placedVertexSequence.GetOrAdd(new VertexSequenceKey(vertices));
            }

            private sealed class UIntSequenceComparer : IEqualityComparer<List<uint>>
            {
                bool IEqualityComparer<List<uint>>.Equals(List<uint> x, List<uint> y)
                {
                    if (x.Count != y.Count)
                        return false;

                    for (int i = 0; i < x.Count; i++)
                    {
                        if (x[i] != y[i])
                            return false;
                    }
                    return true;
                }

                int IEqualityComparer<List<uint>>.GetHashCode(List<uint> obj)
                {
                    int hashcode = 0x42284781;
                    foreach (uint u in obj)
                    {
                        hashcode ^= (int)u;
                        hashcode = int.RotateLeft(hashcode, 5);
                    }

                    return hashcode;
                }
            }
            private NodeCache<List<uint>, NativeLayoutPlacedVertexSequenceOfUIntVertexNode> _placedUIntVertexSequence;
            internal NativeLayoutPlacedVertexSequenceOfUIntVertexNode PlacedUIntVertexSequence(List<uint> uints)
            {
                return _placedUIntVertexSequence.GetOrAdd(uints);
            }

            private NodeCache<MethodDesc, NativeLayoutMethodEntryVertexNode> _methodEntries;
            internal NativeLayoutMethodEntryVertexNode MethodEntry(MethodDesc method)
            {
                return _methodEntries.GetOrAdd(method);
            }

            private NodeCache<MethodDesc, NativeLayoutTemplateMethodSignatureVertexNode> _templateMethodEntries;
            internal NativeLayoutTemplateMethodSignatureVertexNode TemplateMethodEntry(MethodDesc method)
            {
                return _templateMethodEntries.GetOrAdd(method);
            }

            private NodeCache<MethodDesc, NativeLayoutTemplateMethodLayoutVertexNode> _templateMethodLayouts;
            public NativeLayoutTemplateMethodLayoutVertexNode TemplateMethodLayout(MethodDesc method)
            {
                return _templateMethodLayouts.GetOrAdd(method);
            }

            private NodeCache<TypeDesc, NativeLayoutTemplateTypeLayoutVertexNode> _templateTypeLayouts;
            public NativeLayoutTemplateTypeLayoutVertexNode TemplateTypeLayout(TypeDesc type)
            {
                return _templateTypeLayouts.GetOrAdd(GenericTypesTemplateMap.ConvertArrayOfTToRegularArray(_factory, type));
            }

            private NodeCache<TypeDesc, NativeLayoutTypeHandleGenericDictionarySlotNode> _typeHandle_GenericDictionarySlots;
            public NativeLayoutTypeHandleGenericDictionarySlotNode TypeHandleDictionarySlot(TypeDesc type)
            {
                return _typeHandle_GenericDictionarySlots.GetOrAdd(type);
            }

            private NodeCache<TypeDesc, NativeLayoutGcStaticsGenericDictionarySlotNode> _gcStatic_GenericDictionarySlots;
            public NativeLayoutGcStaticsGenericDictionarySlotNode GcStaticDictionarySlot(TypeDesc type)
            {
                return _gcStatic_GenericDictionarySlots.GetOrAdd(type);
            }

            private NodeCache<TypeDesc, NativeLayoutNonGcStaticsGenericDictionarySlotNode> _nonGcStatic_GenericDictionarySlots;
            public NativeLayoutNonGcStaticsGenericDictionarySlotNode NonGcStaticDictionarySlot(TypeDesc type)
            {
                return _nonGcStatic_GenericDictionarySlots.GetOrAdd(type);
            }

            private NodeCache<TypeDesc, NativeLayoutUnwrapNullableGenericDictionarySlotNode> _unwrapNullable_GenericDictionarySlots;
            public NativeLayoutUnwrapNullableGenericDictionarySlotNode UnwrapNullableTypeDictionarySlot(TypeDesc type)
            {
                return _unwrapNullable_GenericDictionarySlots.GetOrAdd(type);
            }

            private NodeCache<TypeDesc, NativeLayoutAllocateObjectGenericDictionarySlotNode> _allocateObject_GenericDictionarySlots;
            public NativeLayoutAllocateObjectGenericDictionarySlotNode AllocateObjectDictionarySlot(TypeDesc type)
            {
                return _allocateObject_GenericDictionarySlots.GetOrAdd(type);
            }

            private NodeCache<TypeDesc, NativeLayoutThreadStaticBaseIndexDictionarySlotNode> _threadStaticIndex_GenericDictionarySlots;
            public NativeLayoutThreadStaticBaseIndexDictionarySlotNode ThreadStaticBaseIndexDictionarySlotNode(TypeDesc type)
            {
                return _threadStaticIndex_GenericDictionarySlots.GetOrAdd(type);
            }

            private NodeCache<TypeDesc, NativeLayoutDefaultConstructorGenericDictionarySlotNode> _defaultConstructor_GenericDictionarySlots;
            public NativeLayoutDefaultConstructorGenericDictionarySlotNode DefaultConstructorDictionarySlot(TypeDesc type)
            {
                return _defaultConstructor_GenericDictionarySlots.GetOrAdd(type);
            }

            private NodeCache<MethodDesc, NativeLayoutInterfaceDispatchGenericDictionarySlotNode> _interfaceCell_GenericDictionarySlots;
            public NativeLayoutInterfaceDispatchGenericDictionarySlotNode InterfaceCellDictionarySlot(MethodDesc method)
            {
                return _interfaceCell_GenericDictionarySlots.GetOrAdd(method);
            }

            private NodeCache<MethodDesc, NativeLayoutMethodDictionaryGenericDictionarySlotNode> _methodDictionary_GenericDictionarySlots;
            public NativeLayoutMethodDictionaryGenericDictionarySlotNode MethodDictionaryDictionarySlot(MethodDesc method)
            {
                return _methodDictionary_GenericDictionarySlots.GetOrAdd(method);
            }

            public readonly NativeLayoutNotSupportedDictionarySlotNode NotSupportedDictionarySlot = new NativeLayoutNotSupportedDictionarySlotNode();

            private struct MethodEntrypointSlotKey : IEquatable<MethodEntrypointSlotKey>
            {
                public readonly bool Unboxing;
                public readonly MethodDesc Method;
                public readonly IMethodNode FunctionPointerTarget;

                public MethodEntrypointSlotKey(MethodDesc method, bool unboxing, IMethodNode functionPointerTarget)
                {
                    Unboxing = unboxing;
                    Method = method;
                    FunctionPointerTarget = functionPointerTarget;
                }

                public override bool Equals(object obj)
                {
                    MethodEntrypointSlotKey? other = obj as MethodEntrypointSlotKey?;
                    if (other.HasValue)
                        return Equals(other.Value);
                    else
                        return false;
                }

                public bool Equals(MethodEntrypointSlotKey other)
                {
                    if (other.Unboxing != Unboxing)
                        return false;

                    if (other.Method != Method)
                        return false;

                    if (other.FunctionPointerTarget != FunctionPointerTarget)
                        return false;

                    return true;
                }

                public override int GetHashCode()
                {
                    int hashCode = Method.GetHashCode() ^ (Unboxing ? 1 : 0);
                    if (FunctionPointerTarget != null)
                        hashCode ^= FunctionPointerTarget.GetHashCode();
                    return hashCode;
                }
            }

            private NodeCache<MethodEntrypointSlotKey, NativeLayoutMethodEntrypointGenericDictionarySlotNode> _methodEntrypoint_GenericDictionarySlots;
            public NativeLayoutMethodEntrypointGenericDictionarySlotNode MethodEntrypointDictionarySlot(MethodDesc method, bool unboxing, IMethodNode functionPointerTarget)
            {
                return _methodEntrypoint_GenericDictionarySlots.GetOrAdd(new MethodEntrypointSlotKey(method, unboxing, functionPointerTarget));
            }

            private NodeCache<FieldDesc, NativeLayoutFieldLdTokenGenericDictionarySlotNode> _fieldLdToken_GenericDictionarySlots;
            public NativeLayoutFieldLdTokenGenericDictionarySlotNode FieldLdTokenDictionarySlot(FieldDesc field)
            {
                return _fieldLdToken_GenericDictionarySlots.GetOrAdd(field);
            }

            private NodeCache<MethodDesc, NativeLayoutMethodLdTokenGenericDictionarySlotNode> _methodLdToken_GenericDictionarySlots;
            public NativeLayoutMethodLdTokenGenericDictionarySlotNode MethodLdTokenDictionarySlot(MethodDesc method)
            {
                return _methodLdToken_GenericDictionarySlots.GetOrAdd(method);
            }

            private NodeCache<TypeSystemEntity, NativeLayoutDictionarySignatureNode> _dictionarySignatures;
            public NativeLayoutDictionarySignatureNode DictionarySignature(TypeSystemEntity owningMethodOrType)
            {
                return _dictionarySignatures.GetOrAdd(owningMethodOrType);
            }

            private NodeCache<ConstrainedMethodUseKey, NativeLayoutConstrainedMethodDictionarySlotNode> _constrainedMethodUseSlots;
            public NativeLayoutConstrainedMethodDictionarySlotNode ConstrainedMethodUse(MethodDesc constrainedMethod, TypeDesc constraintType, bool directCall)
            {
                return _constrainedMethodUseSlots.GetOrAdd(new ConstrainedMethodUseKey(constrainedMethod, constraintType, directCall));
            }
        }

        public NativeLayoutHelper NativeLayout;
    }
}
