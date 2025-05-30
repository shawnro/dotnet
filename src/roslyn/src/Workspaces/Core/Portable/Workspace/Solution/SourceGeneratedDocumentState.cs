﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.SourceGeneration;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis;

internal sealed class SourceGeneratedDocumentState : DocumentState
{
    /// <summary>
    /// Backing store for <see cref="GetOriginalSourceTextContentHash"/>.
    /// </summary>
    private readonly Lazy<Checksum> _lazyContentHash;

    public SourceGeneratedDocumentIdentity Identity { get; }

    public string HintName => Identity.HintName;

    /// <summary>
    /// It's reasonable to capture 'text' here and keep it alive.  We're already holding onto the generated text
    /// strongly in the ConstantTextAndVersionSource we're passing to our base type. 
    /// </summary>
    public SourceText SourceText { get; }

    /// <summary>
    /// Checksum of <see cref="SourceText"/> when it was <em>originally</em> created.  This is subtly, but importantly
    /// different from the checksum acquired from <see cref="SourceText.GetChecksum"/>.  Specifically, the original
    /// source text may have been created from a <see cref="System.IO.Stream"/> in a lossy fashion (for example,
    /// removing BOM marks and the like) on the OOP side. As such, its checksum might not be reconstructible from the
    /// actual text and hash algorithm that were used to create the SourceText on the host side.  To ensure both the
    /// host and OOP are in agreement about the true content checksum, we store this separately.
    /// </summary>
    public Checksum GetOriginalSourceTextContentHash()
        => _lazyContentHash.Value;

    public readonly DateTime GenerationDateTime;

    public static SourceGeneratedDocumentState Create(
        SourceGeneratedDocumentIdentity documentIdentity,
        SourceText generatedSourceText,
        ParseOptions parseOptions,
        LanguageServices languageServices,
        Checksum? originalSourceTextChecksum,
        DateTime generationDateTime)
    {
        return Create(documentIdentity, generatedSourceText, syntaxNode: null, parseOptions, languageServices, originalSourceTextChecksum, generationDateTime);
    }

    public static SourceGeneratedDocumentState Create(
        SourceGeneratedDocumentIdentity documentIdentity,
        SourceText? generatedSourceText,
        SyntaxNode? syntaxNode,
        ParseOptions parseOptions,
        LanguageServices languageServices,
        Checksum? originalSourceTextChecksum,
        DateTime generationDateTime)
    {
        Contract.ThrowIfTrue(generatedSourceText is null && syntaxNode is null);
        Contract.ThrowIfTrue(generatedSourceText is not null && syntaxNode is not null);

        if (generatedSourceText is null)
        {
            // We don't currently support lazy source text in source generated documents, so just use the syntax to get it
            Contract.ThrowIfNull(syntaxNode);
            generatedSourceText = syntaxNode.GetText();
        }

        // If the caller explicitly provided us with the checksum for the source text, then we always defer to that.
        // This happens on the host side, when we are given the data computed by the OOP side.
        //
        // If the caller didn't provide us with the checksum, then we'll compute it on demand.  This happens on the OOP
        // side when we're actually producing the SG doc in the first place.
        var lazyTextChecksum = new Lazy<Checksum>(() => originalSourceTextChecksum ?? ComputeContentHash(generatedSourceText));
        return Create(documentIdentity, generatedSourceText, syntaxNode, parseOptions, languageServices, lazyTextChecksum, generationDateTime);
    }

    private static SourceGeneratedDocumentState Create(
        SourceGeneratedDocumentIdentity documentIdentity,
        SourceText generatedSourceText,
        SyntaxNode? syntaxNode,
        ParseOptions parseOptions,
        LanguageServices languageServices,
        Lazy<Checksum> lazyTextChecksum,
        DateTime generationDateTime)
    {
        var loadTextOptions = new LoadTextOptions(generatedSourceText.ChecksumAlgorithm);
        var textAndVersion = TextAndVersion.Create(generatedSourceText, VersionStamp.Create());
        var textSource = new ConstantTextAndVersionSource(textAndVersion);

        ITreeAndVersionSource treeSource;
        if (syntaxNode is null)
        {
            treeSource = CreateLazyFullyParsedTree(
                textSource,
                loadTextOptions,
                documentIdentity.FilePath,
                parseOptions,
                languageServices);
        }
        else
        {
            var factory = languageServices.GetRequiredService<ISyntaxTreeFactoryService>();
            var newTree = factory.CreateSyntaxTree(documentIdentity.FilePath, parseOptions, generatedSourceText, generatedSourceText.Encoding, generatedSourceText.ChecksumAlgorithm, syntaxNode);
            treeSource = SimpleTreeAndVersionSource.Create(new TreeAndVersion(newTree, VersionStamp.Create()));
        }

        return new SourceGeneratedDocumentState(
            documentIdentity,
            languageServices,
            documentServiceProvider: SourceGeneratedTextDocumentServiceProvider.Instance,
            new DocumentInfo.DocumentAttributes(
                documentIdentity.DocumentId,
                name: documentIdentity.HintName,
                folders: [],
                parseOptions.Kind,
                filePath: documentIdentity.FilePath,
                isGenerated: true,
                designTimeOnly: false),
            parseOptions,
            textSource,
            generatedSourceText,
            loadTextOptions,
            treeSource,
            lazyTextChecksum,
            generationDateTime);
    }

    private SourceGeneratedDocumentState(
        SourceGeneratedDocumentIdentity documentIdentity,
        LanguageServices languageServices,
        IDocumentServiceProvider? documentServiceProvider,
        DocumentInfo.DocumentAttributes attributes,
        ParseOptions options,
        ITextAndVersionSource textSource,
        SourceText text,
        LoadTextOptions loadTextOptions,
        ITreeAndVersionSource treeSource,
        Lazy<Checksum> lazyContentHash,
        DateTime generationDateTime)
        : base(languageServices, documentServiceProvider, attributes, textSource, loadTextOptions, options, treeSource)
    {
        Identity = documentIdentity;

        SourceText = text;
        _lazyContentHash = lazyContentHash;
        GenerationDateTime = generationDateTime;
    }

    private static Checksum ComputeContentHash(SourceText text)
        => Checksum.From(text.GetContentHash());

    // The base allows for parse options to be null for non-C#/VB languages, but we'll always have parse options
    public new ParseOptions ParseOptions => base.ParseOptions!;

    public SourceGeneratedDocumentContentIdentity GetContentIdentity()
        => new(this.GetOriginalSourceTextContentHash(), this.SourceText.Encoding?.WebName, this.SourceText.ChecksumAlgorithm);

    protected override TextDocumentState UpdateAttributes(DocumentInfo.DocumentAttributes newAttributes)
        => throw new NotSupportedException(WorkspacesResources.The_contents_of_a_SourceGeneratedDocument_may_not_be_changed);

    protected override TextDocumentState UpdateDocumentServiceProvider(IDocumentServiceProvider? newProvider)
        => throw new NotSupportedException(WorkspacesResources.The_contents_of_a_SourceGeneratedDocument_may_not_be_changed);

    protected override TextDocumentState UpdateText(ITextAndVersionSource newTextSource, PreservationMode mode, bool incremental)
        => throw new NotSupportedException(WorkspacesResources.The_contents_of_a_SourceGeneratedDocument_may_not_be_changed);

    public SourceGeneratedDocumentState WithText(SourceText sourceText)
    {
        // See if we can reuse this instance directly
        var newSourceTextChecksum = ComputeContentHash(sourceText);
        if (this.GetOriginalSourceTextContentHash() == newSourceTextChecksum)
            return this;

        return Create(
            Identity,
            sourceText,
            ParseOptions,
            LanguageServices,
            // Just pass along the checksum for the new source text since we've already computed it.
            newSourceTextChecksum,
            GenerationDateTime);
    }

    public SourceGeneratedDocumentState WithParseOptions(ParseOptions parseOptions)
    {
        // See if we can reuse this instance directly
        if (ParseOptions.Equals(parseOptions))
            return this;

        return Create(
            Identity,
            SourceText,
            syntaxNode: null,
            parseOptions,
            LanguageServices,
            // We're just changing the parse options.  So the checksum will remain as is.
            _lazyContentHash,
            GenerationDateTime);
    }

    public SourceGeneratedDocumentState WithGenerationDateTime(DateTime generationDateTime)
    {
        // See if we can reuse this instance directly
        if (this.GenerationDateTime == generationDateTime)
            return this;

        // Copy over all state as-is.  The generation time doesn't change any actual state (the same tree will be
        // produced for example).
        return new(
            this.Identity,
            this.LanguageServices,
            this.DocumentServiceProvider,
            this.Attributes,
            this.ParseOptions,
            this.TextAndVersionSource,
            this.SourceText,
            this.LoadTextOptions,
            this.TreeSource!,
            this._lazyContentHash,
            generationDateTime);
    }

    public SourceGeneratedDocumentState WithSyntaxRoot(SyntaxNode newRoot)
    {
        // See if we can reuse this instance directly
        if (this.TryGetSyntaxTree(out var tree) &&
            tree.TryGetRoot(out var root) &&
            root == newRoot)
        {
            return this;
        }

        var sourceText = newRoot.GetText();
        var factory = LanguageServices.GetRequiredService<ISyntaxTreeFactoryService>();
        var newTree = factory.CreateSyntaxTree(Attributes.SyntaxTreeFilePath, ParseOptions, sourceText, sourceText.Encoding, sourceText.ChecksumAlgorithm, newRoot);

        var lazyTextChecksum = new Lazy<Checksum>(() => ComputeContentHash(sourceText));
        var textAndVersion = TextAndVersion.Create(sourceText, VersionStamp.Create());
        var textSource = new ConstantTextAndVersionSource(textAndVersion);
        var newTreeVersion = GetNewTreeVersionForUpdatedTree(newRoot, GetNewerVersion(), PreservationMode.PreserveValue);
        var newTreeSource = SimpleTreeAndVersionSource.Create(new TreeAndVersion(newTree, newTreeVersion));

        return new(
            this.Identity,
            this.LanguageServices,
            this.DocumentServiceProvider,
            this.Attributes,
            this.ParseOptions,
            textSource,
            sourceText,
            this.LoadTextOptions,
            newTreeSource,
            lazyTextChecksum,
            this.GenerationDateTime);
    }

    /// <summary>
    /// This is modeled after <see cref="DefaultTextDocumentServiceProvider"/>, but sets
    /// <see cref="IDocumentOperationService.CanApplyChange"/> to <see langword="false"/> for source generated
    /// documents.
    /// </summary>
    internal sealed class SourceGeneratedTextDocumentServiceProvider : IDocumentServiceProvider
    {
        public static readonly SourceGeneratedTextDocumentServiceProvider Instance = new();

        private SourceGeneratedTextDocumentServiceProvider()
        {
        }

        public TService? GetService<TService>()
            where TService : class, IDocumentService
        {
            if (SourceGeneratedDocumentOperationService.Instance is TService documentOperationService)
            {
                return documentOperationService;
            }

            if (DocumentPropertiesService.Default is TService documentPropertiesService)
            {
                return documentPropertiesService;
            }

            return null;
        }

        private sealed class SourceGeneratedDocumentOperationService : IDocumentOperationService
        {
            public static readonly SourceGeneratedDocumentOperationService Instance = new();

            public bool CanApplyChange => false;
            public bool SupportDiagnostics => true;
        }
    }
}
