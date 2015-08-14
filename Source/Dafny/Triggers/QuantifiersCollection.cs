﻿#define QUANTIFIER_WARNINGS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Boogie;
using System.Diagnostics.Contracts;

//FIXME Generated triggers should be _triggers
//FIXME: When scoring, do not consider old(x) to be higher than x.

namespace Microsoft.Dafny.Triggers {
  class QuantifierWithTriggers {
    internal QuantifierExpr quantifier;
    internal List<TriggerTerm> CandidateTerms;
    internal List<TriggerCandidate> Candidates;
    internal List<TriggerCandidate> RejectedCandidates;

    internal bool AllowsLoops { get { return quantifier.Attributes.AsEnumerable().Any(a => a.Name == "loop"); } }
    internal bool CouldSuppressLoops { get; set; }

    internal QuantifierWithTriggers(QuantifierExpr quantifier) {
      this.quantifier = quantifier;
      this.RejectedCandidates = new List<TriggerCandidate>();
    }

    internal void TrimInvalidTriggers() {
      Contract.Requires(CandidateTerms != null);
      Contract.Requires(Candidates != null);
      Candidates = TriggerUtils.Filter(Candidates, tr => tr.MentionsAll(quantifier.BoundVars), tr => { }).ToList();
    }
  }

  class QuantifiersCollection {
    readonly ErrorReporter reporter;
    readonly List<QuantifierWithTriggers> quantifiers;
   
    internal QuantifiersCollection(IEnumerable<QuantifierExpr> quantifiers, ErrorReporter reporter) {
      this.reporter = reporter;
      this.quantifiers = quantifiers.Select(q => new QuantifierWithTriggers(q)).ToList();
    }

    internal void ComputeTriggers(TriggersCollector triggersCollector) {
      CollectAndShareTriggers(triggersCollector);
      TrimInvalidTriggers();
      BuildDependenciesGraph();
      SuppressMatchingLoops();
      SelectTriggers();
    }
    
    private bool SubsetGenerationPredicate(List<TriggerTerm> terms, TriggerTerm additionalTerm) {
      // Simple formulas like [P0(i) && P1(i) && P2(i) && P3(i) && P4(i)] yield very
      // large numbers of multi-triggers, most of which are useless. This filter
      // restricts subsets of terms so that we only generate sets of terms where each
      // element contributes at least one variable. In the example above, we'll only
      // get 5 triggers.
      return additionalTerm.Variables.Where(v => !terms.Any(t => t.Variables.Contains(v))).Any();
    }

    //FIXME document that this assumes that the quantifiers live in the same context and share the same variables
    void CollectAndShareTriggers(TriggersCollector triggersCollector) {
      var pool = quantifiers.SelectMany(q => triggersCollector.CollectTriggers(q.quantifier));
      var distinctPool = pool.Deduplicate(TriggerTerm.Eq);
      var multiPool = TriggerUtils.AllNonEmptySubsets(distinctPool, SubsetGenerationPredicate).Select(candidates => new TriggerCandidate(candidates)).ToList();

      foreach (var q in quantifiers) {
        q.CandidateTerms = distinctPool;
        q.Candidates = multiPool;
      }
    }

    private void TrimInvalidTriggers() {
      foreach (var q in quantifiers) {
        q.TrimInvalidTriggers();
      }
    }

    void BuildDependenciesGraph() {
      // FIXME: Think more about multi-quantifier dependencies
      //class QuantifierDependency {
      //  QuantifierWithTriggers Cause;
      //  QuantifierWithTriggers Consequence;
      //  List<TriggerCandidate> Triggers;
      //  List<Expression> MatchingTerm;
      //}
    }

    void SuppressMatchingLoops() {
      // NOTE: This only looks for self-loops; that is, loops involving a single
      // quantifier.

      // For a given quantifier q, we introduce a triggering relation between trigger
      // candidates by writing t1 → t2 if instantiating q from t1 introduces a ground
      // term that matches t2. Then, we notice that this relation is transitive, since
      // all triggers yield the same set of terms. This means that any matching loop
      // t1 → ... → t1 can be reduced to a self-loop t1 → t1. Detecting such
      // self-loops is then only a matter of finding terms in the body of the
      // quantifier that match a given trigger.

      // Of course, each trigger that actually appears in the body of the quantifier
      // yields a trivial self-loop (e.g. P(i) in [∀ i {P(i)} ⋅ P(i)]), so we
      // ignore this type of loops. In fact, we ignore any term in the body of the
      // quantifier that matches one of the terms of the trigger (this ensures that
      // [∀ x {f(x), f(f(x))} ⋅ f(x) = f(f(x))] is not a loop). And we even
      // ignore terms that almost match a trigger term, modulo a single variable
      // (this ensures that [∀ x y {a(x, y)} ⋅ a(x, y) == a(y, x)] is not a loop).
      // This ignoring logic is implemented by the CouldCauseLoops method.

      foreach (var q in quantifiers) {
        var looping = new List<TriggerCandidate>();
        var loopingSubterms = q.Candidates.ToDictionary(candidate => candidate, candidate => candidate.LoopingSubterms(q.quantifier).ToList());

        var safe = TriggerUtils.Filter(
          q.Candidates,
          c => !loopingSubterms[c].Any(),
          c => {
            looping.Add(c);
            c.Annotation = "loop with " + loopingSubterms[c].MapConcat(t => Printer.ExprToString(t.Expr), ", ");
          }).ToList();

        q.CouldSuppressLoops = safe.Count > 0;

        if (!q.AllowsLoops && q.CouldSuppressLoops) {
          q.Candidates = safe;
          q.RejectedCandidates.AddRange(looping);
        }
      }
    }

    void SelectTriggers() {
      //FIXME
    }

    private void CommitOne(QuantifierWithTriggers q, object conjunctId) {
      var errorLevel = ErrorLevel.Info;
      var msg = new StringBuilder();
      var indent = conjunctId != null ? "    " : "  ";
      var header = conjunctId != null ? String.Format("  For conjunct {0}:{1}", conjunctId, Environment.NewLine) : "";

      if (!TriggerUtils.NeedsAutoTriggers(q.quantifier)) { //FIXME: no_trigger is passed down to Boogie
        msg.Append(indent).AppendLine("Not generating triggers for this quantifier.");
      } else {
        foreach (var candidate in q.Candidates) {
          q.quantifier.Attributes = new Attributes("trigger", candidate.Terms.Select(t => t.Expr).ToList(), q.quantifier.Attributes);
        }

        if (q.Candidates.Any()) {
          msg.Append(indent).AppendLine("Selected triggers:");
          foreach (var mc in q.Candidates) {
            msg.Append(indent).Append("  ").AppendLine(mc.ToString());
          }
        }

        if (q.RejectedCandidates.Any()) {
          msg.Append(indent).AppendLine("Rejected triggers:");
          foreach (var mc in q.RejectedCandidates) {
            msg.Append(indent).Append("  ").AppendLine(mc.ToString());
          }
        }

#if QUANTIFIER_WARNINGS
        if (!q.CandidateTerms.Any()) {
          errorLevel = ErrorLevel.Warning;
          msg.Append(indent).AppendLine("No terms found to trigger on.");
        } else if (!q.Candidates.Any()) {
          errorLevel = ErrorLevel.Warning;
          msg.Append(indent).AppendLine("No trigger covering all quantified variables found.");
        } else if (!q.CouldSuppressLoops) {
          errorLevel = ErrorLevel.Warning;
          msg.Append(indent).AppendLine("Suppressing loops would leave this quantifier without triggers.");
        }
#endif
      }

      if (msg.Length > 0) {
        reporter.Message(MessageSource.Rewriter, errorLevel, q.quantifier.tok, header + msg.ToString());
      }
    }

    internal void CommitTriggers() {
      foreach (var q in quantifiers) {
        CommitOne(q, quantifiers.Count > 1 ? q.quantifier : null);
      }
    }
  }
}
