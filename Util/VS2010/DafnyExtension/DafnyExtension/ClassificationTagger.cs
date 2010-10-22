﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;


namespace DafnyLanguage
{
  [Export(typeof(ITaggerProvider))]
  [ContentType("dafny")]
  [TagType(typeof(ClassificationTag))]
  internal sealed class DafnyClassifierProvider : ITaggerProvider
  {
    [Import]
    internal IBufferTagAggregatorFactoryService AggregatorFactory = null;

    [Import]
    internal IClassificationTypeRegistryService ClassificationTypeRegistry = null;

    public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag {
      ITagAggregator<DafnyTokenTag> tagAggregator = AggregatorFactory.CreateTagAggregator<DafnyTokenTag>(buffer);
      return new DafnyClassifier(buffer, tagAggregator, ClassificationTypeRegistry) as ITagger<T>;
    }
  }

  internal sealed class DafnyClassifier : ITagger<ClassificationTag>
  {
    ITextBuffer _buffer;
    ITagAggregator<DafnyTokenTag> _aggregator;
    IDictionary<DafnyTokenKinds, IClassificationType> _typeMap;

    internal DafnyClassifier(ITextBuffer buffer,
                             ITagAggregator<DafnyTokenTag> tagAggregator,
                             IClassificationTypeRegistryService typeService) {
      _buffer = buffer;
      _aggregator = tagAggregator;
      _aggregator.TagsChanged += new EventHandler<TagsChangedEventArgs>(_aggregator_TagsChanged);
      _typeMap = new Dictionary<DafnyTokenKinds, IClassificationType>();
      _typeMap[DafnyTokenKinds.Keyword] = typeService.GetClassificationType("keyword");  // use the built-in "keyword" classification type
      _typeMap[DafnyTokenKinds.Number] = typeService.GetClassificationType("number");  // use the built-in "number" classification type
      _typeMap[DafnyTokenKinds.String] = typeService.GetClassificationType("string");  // use the built-in "string" classification type
      _typeMap[DafnyTokenKinds.Comment] = typeService.GetClassificationType("comment");  // use the built-in "comment" classification type
    }

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    public IEnumerable<ITagSpan<ClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans) {
      if (spans.Count == 0) yield break;
      var snapshot = spans[0].Snapshot;
      foreach (var tagSpan in this._aggregator.GetTags(spans)) {
        IClassificationType t = _typeMap[tagSpan.Tag.Kind];
        foreach (SnapshotSpan s in tagSpan.Span.GetSpans(snapshot)) {
          yield return new TagSpan<ClassificationTag>(s, new ClassificationTag(t));
        }
      }
    }

    void _aggregator_TagsChanged(object sender, TagsChangedEventArgs e) {
      var chng = TagsChanged;
      if (chng != null) {
        NormalizedSnapshotSpanCollection spans = e.Span.GetSpans(_buffer.CurrentSnapshot);
        if (spans.Count > 0) {
          SnapshotSpan span = new SnapshotSpan(spans[0].Start, spans[spans.Count - 1].End);
          chng(this, new SnapshotSpanEventArgs(span));
        }
      }
    }
  }

#if false  // the commented-out code here shows show to define new classifier types; however, the Dafny mode just uses the built-in "keyword" and "number" classifier types
  /// <summary>
  /// Defines an editor format for the keyword type.
  /// </summary>
  [Export(typeof(EditorFormatDefinition))]
  [ClassificationType(ClassificationTypeNames = "Dafny-keyword")]
  [Name("Dafny-keyword")]
  //this should be visible to the end user
  [UserVisible(false)]
  //set the priority to be after the default classifiers
  [Order(Before = Priority.Default)]
  internal sealed class Keyword : ClassificationFormatDefinition
  {
    /// <summary>
    /// Defines the visual format for the "ordinary" classification type
    /// </summary>
    public Keyword() {
      this.DisplayName = "Dafny keyword"; //human readable version of the name
      this.ForegroundColor = Colors.BlueViolet;
    }
  }

  /// <summary>
  /// Defines an editor format for the OrdinaryClassification type that has a purple background
  /// and is underlined.
  /// </summary>
  [Export(typeof(EditorFormatDefinition))]
  [ClassificationType(ClassificationTypeNames = "Dafny-number")]
  [Name("Dafny-number")]
  //this should be visible to the end user
  [UserVisible(false)]
  //set the priority to be after the default classifiers
  [Order(Before = Priority.Default)]
  internal sealed class Number : ClassificationFormatDefinition
  {
    /// <summary>
    /// Defines the visual format for the "ordinary" classification type
    /// </summary>
    public Number() {
      this.DisplayName = "Dafny numeric literal"; //human readable version of the name
      this.ForegroundColor = Colors.Orange;
    }
  }

  internal static class ClassificationDefinition
  {
    /// <summary>
    /// Defines the "ordinary" classification type.
    /// </summary>
    [Export(typeof(ClassificationTypeDefinition))]
    [Name("Dafny-keyword")]
    internal static ClassificationTypeDefinition Keyword = null;

    /// <summary>
    /// Defines the "ordinary" classification type.
    /// </summary>
    [Export(typeof(ClassificationTypeDefinition))]
    [Name("Dafny-number")]
    internal static ClassificationTypeDefinition Number = null;
  }
#endif
}
