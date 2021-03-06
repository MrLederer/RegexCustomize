﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework.XamlTypes;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using PatternCustomizer.State;
using Microsoft.VisualStudio.Text.Classification;
using System.ComponentModel;

namespace PatternCustomizer
{
    [Export(typeof(ITaggerProvider))]
    [ContentType("CSharp")]
    [TagType(typeof(IClassificationTag))]
    internal class RegexCustomizerProvider : ITaggerProvider
        //IViewTaggerProvider
    {
#pragma warning disable CS0649
        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry; // this some Visual studio MEF magic
#pragma warning restore CS0649

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            return (ITagger<T>)new PatternCustomizer(buffer, ClassificationRegistry);
        }
    }

    class PatternCustomizer : ITagger<IClassificationTag>
    {
        private readonly ITextBuffer _buffer;
        private readonly IClassificationTypeRegistryService _registery;
        private IDictionary<IRule, IClassificationType> _ruleToFormatType;
#pragma warning disable CS0067
        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;
#pragma warning restore CS0067

        internal PatternCustomizer(ITextBuffer buffer, IClassificationTypeRegistryService registry)
        {
            var state = PatternCustomizerPackage.currentState;
            _buffer = buffer;
            _registery = registry;
            _ruleToFormatType = UpdatedRulesToPattern(_registery, state);
            state.OrderedPatternToStyleMapping.ListChanged += this.UpdateRulesToPattern;
        }

        private void UpdateRulesToPattern(object sender, ListChangedEventArgs e)
        {
            _ruleToFormatType = UpdatedRulesToPattern(_registery, PatternCustomizerPackage.currentState);
        }

        private static IDictionary<IRule, IClassificationType> UpdatedRulesToPattern(IClassificationTypeRegistryService registry, IState state)
        {
            return state.GetEnabledDeclaredFormatNames()
                            .Select(formatName => (name: formatName, type: registry.GetClassificationType(formatName)))
                            .SelectMany(format => state
                                .GetRules(format.name)
                                .Select(rule => (rule, formatType: format.type)))
                            .ToDictionaryWithKeyOverwritting(_ => _.rule, _ => _.formatType);
        }

        public IEnumerable<ITagSpan<IClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
            {
                yield break;
            }
            // TODO: implement cache

            foreach (var span in spans)
            {
                if (span.Length == 0)
                {
                    continue;
                }
                foreach (var line in span.Snapshot.Lines)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    var lineSpanshot = line.Snapshot;
                    Func<Match, SnapshotSpan> tagger = (Match match) => new SnapshotSpan(lineSpanshot, line.Start + match.Index, match.Length);

                    var tagsAndFormats = _ruleToFormatType
                        .Select(ruleToFormat => (matches: ruleToFormat.Key.Detect(line.GetText()), formatType: ruleToFormat.Value))
                        .SelectMany(_ => _.matches.Select(match => (match, _.formatType)))
                        .Select(_ => (tag: tagger(_.match), _.formatType));

                    foreach (var (tag, format) in tagsAndFormats)
                    {
                        yield return new TagSpan<IClassificationTag>(tag, new ClassificationTag(format));
                    }
                }
            }
            yield break;
        }
    }
}
