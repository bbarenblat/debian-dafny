﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Boogie;
using Bpl = Microsoft.Boogie;

namespace Microsoft.Dafny {
  // FIXME: This should not be duplicated here
  class DafnyConsolePrinter : ConsolePrinter {
    public override void ReportBplError(IToken tok, string message, bool error, TextWriter tw, string category = null) {
      // Dafny has 0-indexed columns, but Boogie counts from 1
      var realigned_tok = new Token(tok.line, tok.col - 1);
      realigned_tok.kind = tok.kind;
      realigned_tok.pos = tok.pos;
      realigned_tok.val = tok.val;
      realigned_tok.filename = tok.filename;
      base.ReportBplError(realigned_tok, message, error, tw, category);

      if (tok is Dafny.NestedToken) {
        var nt = (Dafny.NestedToken)tok;
        ReportBplError(nt.Inner, "Related location", false, tw);
      }
    }
  }

  class DafnyHelper {
    private string fname;
    private string source;

    private Dafny.Errors errors;
    private Dafny.Program dafnyProgram;
    private Bpl.Program boogieProgram;

    public DafnyHelper(string fname, string source) {
      this.fname = fname;
      this.source = source;
      this.errors = new Dafny.Errors();
    }

    public bool Verify() {
      return Parse() && Resolve() && Translate() && Boogie();
    }

    private bool Parse() {
      Dafny.ModuleDecl module = new Dafny.LiteralModuleDecl(new Dafny.DefaultModuleDecl(), null);
      Dafny.BuiltIns builtIns = new Dafny.BuiltIns();
      var success = (Dafny.Parser.Parse(source, fname, fname, module, builtIns, errors) == 0 &&
                     Dafny.Main.ParseIncludes(module, builtIns, new List<string>(), errors) == null);
      if (success) {
        dafnyProgram = new Dafny.Program(fname, module, builtIns);
      }
      return success;
    }

    private bool Resolve() {
      var resolver = new Dafny.Resolver(dafnyProgram);
      resolver.ResolveProgram(dafnyProgram);
      return resolver.ErrorCount == 0;
    }

    private bool Translate() {
      var translator = new Dafny.Translator() { InsertChecksums = true, UniqueIdPrefix = null }; //FIXME check if null is OK for UniqueIdPrefix
      boogieProgram = translator.Translate(dafnyProgram); // FIXME how are translation errors reported?
      return true;
    }

    private bool Boogie() {
      if (boogieProgram.Resolve() == 0 && boogieProgram.Typecheck() == 0) { //FIXME ResolveAndTypecheck?
        ExecutionEngine.EliminateDeadVariables(boogieProgram);
        ExecutionEngine.CollectModSets(boogieProgram);
        ExecutionEngine.CoalesceBlocks(boogieProgram);
        ExecutionEngine.Inline(boogieProgram);

        switch (ExecutionEngine.InferAndVerify(boogieProgram, new PipelineStatistics(), "ServerProgram", null, DateTime.UtcNow.Ticks.ToString())) { // FIXME check if null is ok for error delegate
          case PipelineOutcome.Done:
          case PipelineOutcome.VerificationCompleted:
            return true;
        }
      }

      return false;
    }
  }
}
