﻿//-----------------------------------------------------------------------------
//
// Copyright (C) Microsoft Corporation.  All Rights Reserved.
//
//-----------------------------------------------------------------------------
// This file contains the transformations that are applied to a module that is
// constructed as a refinement of another.  It is invoked during program resolution,
// so the transformation is done syntactically.  Upon return from the RefinementTransformer,
// the caller is expected to resolve the resulting module.
//
// As for now (and perhaps this is always the right thing to do), attributes do
// not survive the transformation.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Diagnostics.Contracts;
using IToken = Microsoft.Boogie.IToken;

namespace Microsoft.Dafny {
  public class RefinementToken : IToken
  {
    IToken tok;
    public RefinementToken(IToken tok)
    {
      this.tok = tok;
    }

    public int kind {
      get { return tok.kind; }
      set { throw new NotSupportedException(); }
    }
    public string filename {
      get { return tok.filename; }
      set { throw new NotSupportedException(); }
    }
    public int pos {
      get { return tok.pos; }
      set { throw new NotSupportedException(); }
    }
    public int col {
      get { return tok.col; }
      set { throw new NotSupportedException(); }
    }
    public int line {
      get { return tok.line; }
      set { throw new NotSupportedException(); }
    }
    public string/*!*/ val {
      get { return tok.val; }
      set { throw new NotSupportedException(); }
    }
    public bool IsValid {
      get { return tok.IsValid; }
    }
  }

  public class RefinementTransformer
  {
    ResolutionErrorReporter reporter;
    public RefinementTransformer(ResolutionErrorReporter reporter) {
      Contract.Requires(reporter != null);
      this.reporter = reporter;
    }

    public void Construct(ModuleDecl m) {
      Contract.Requires(m != null);
      Contract.Requires(m.RefinementBase != null);

      var prev = m.RefinementBase;

      // Include the imports of the base.  Note, prev is itself NOT added as an import
      // of m; instead, the contents from prev is merged directly into m.
      // (Here, we change the import declarations.  But edges for these imports will
      // not be added to the importGraph of the calling resolver.  However, the refines
      // clause gave rise to an edge in the importGraph, so the transitive import edges
      // are represented in the importGraph.)
      foreach (var im in prev.Imports) {
        if (!m.ImportNames.Contains(im.Name)) {
          m.ImportNames.Add(im.Name);
          m.Imports.Add(im);
        }
      }

      // Create a simple name-to-decl dictionary.  Ignore any duplicates at this time.
      var declaredNames = new Dictionary<string, int>();
      for (int i = 0; i < m.TopLevelDecls.Count; i++) {
        var d = m.TopLevelDecls[i];
        if (!declaredNames.ContainsKey(d.Name)) {
          declaredNames.Add(d.Name, i);
        }
      }

      // Merge the declarations of prev into the declarations of m
      foreach (var d in prev.TopLevelDecls) {
        int index;
        if (!declaredNames.TryGetValue(d.Name, out index)) {
          m.TopLevelDecls.Add(CloneDeclaration(d, m));
        } else {
          var nw = m.TopLevelDecls[index];
          if (d is ArbitraryTypeDecl) {
            // this is allowed to be refined by any type declaration, so just keep the new one
          } else if (nw is ArbitraryTypeDecl) {
            reporter.Error(nw, "an arbitrary type declaration ({0}) in a refining module cannot replace a more specific type declaration in the refinement base", nw.Name);
          } else if (nw is DatatypeDecl) {
            if (d is ClassDecl) {
              reporter.Error(nw, "a datatype declaration ({0}) in a refining module cannot replace a class declaration in the refinement base", nw.Name);
            } else {
              m.TopLevelDecls[index] = MergeDatatype((DatatypeDecl)nw, (DatatypeDecl)d);
            }
          } else {
            Contract.Assert(nw is ClassDecl);
            if (d is DatatypeDecl) {
              reporter.Error(nw, "a class declaration ({0}) in a refining module cannot replace a datatype declaration in the refinement base", nw.Name);
            } else {
              m.TopLevelDecls[index] = MergeClass((ClassDecl)nw, (ClassDecl)d);
            }
          }
        }
      }
    }

    IToken Tok(IToken tok) {
      return new RefinementToken(tok);
    }

    // -------------------------------------------------- Cloning ---------------------------------------------------------------

    TopLevelDecl CloneDeclaration(TopLevelDecl d, ModuleDecl m) {
      Contract.Requires(d != null);
      Contract.Requires(m != null);

      if (d is ArbitraryTypeDecl) {
        var dd = (ArbitraryTypeDecl)d;
        return new ArbitraryTypeDecl(Tok(dd.tok), dd.Name, m, null);
      } else if (d is DatatypeDecl) {
        var dd = (DatatypeDecl)d;
        var tps = dd.TypeArgs.ConvertAll(CloneTypeParam);
        var ctors = dd.Ctors.ConvertAll(CloneCtor);
        var dt = new DatatypeDecl(Tok(dd.tok), dd.Name, m, tps, ctors, null);
        return dt;
      } else if (d is ClassDecl) {
        var dd = (ClassDecl)d;
        var tps = dd.TypeArgs.ConvertAll(CloneTypeParam);
        var mm = dd.Members.ConvertAll(CloneMember);
        var cl = new ClassDecl(Tok(dd.tok), dd.Name, m, tps, mm, null);
        return cl;
      } else {
        Contract.Assert(false);  // unexpected declaration
        return null;  // to please compiler
      }
    }

    DatatypeCtor CloneCtor(DatatypeCtor ct) {
      return new DatatypeCtor(Tok(ct.tok), ct.Name, ct.Formals.ConvertAll(CloneFormal), null);
    }

    TypeParameter CloneTypeParam(TypeParameter tp) {
      return new TypeParameter(Tok(tp.tok), tp.Name);
    }

    MemberDecl CloneMember(MemberDecl member) {
      if (member is Field) {
        var f = (Field)member;
        return new Field(Tok(f.tok), f.Name, f.IsGhost, f.IsMutable, CloneType(f.Type), null);
      } else if (member is Function) {
        var f = (Function)member;
        var tps = f.TypeArgs.ConvertAll(CloneTypeParam);
        return new Function(Tok(f.tok), f.Name, f.IsStatic, f.IsGhost, f.IsUnlimited, tps, f.Formals.ConvertAll(CloneFormal), CloneType(f.ResultType),
          f.Req.ConvertAll(CloneExpr), f.Reads.ConvertAll(CloneFrameExpr), f.Ens.ConvertAll(CloneExpr), CloneSpecExpr(f.Decreases), CloneExpr(f.Body), null);
      } else {
        var m = (Method)member;
        var tps = m.TypeArgs.ConvertAll(CloneTypeParam);
        return new Method(Tok(m.tok), m.Name, m.IsStatic, m.IsGhost, tps, m.Ins.ConvertAll(CloneFormal), m.Outs.ConvertAll(CloneFormal),
          m.Req.ConvertAll(CloneMayBeFreeExpr), CloneSpecFrameExpr(m.Mod), m.Ens.ConvertAll(CloneMayBeFreeExpr), CloneSpecExpr(m.Decreases), CloneBlockStmt(m.Body), null);
      }
    }

    Type CloneType(Type t) {
      if (t is BasicType) {
        return t;
      } else if (t is SetType) {
        var tt = (SetType)t;
        return new SetType(tt.Arg);
      } else if (t is SeqType) {
        var tt = (SeqType)t;
        return new SeqType(tt.Arg);
      } else if (t is MultiSetType) {
        var tt = (MultiSetType)t;
        return new MultiSetType(tt.Arg);
      } else if (t is UserDefinedType) {
        var tt = (UserDefinedType)t;
        return new UserDefinedType(Tok(tt.tok), tt.Name, tt.TypeArgs.ConvertAll(CloneType));
      } else if (t is InferredTypeProxy) {
        return new InferredTypeProxy();
      } else {
        Contract.Assert(false);  // unexpected type (e.g., no other type proxies are expected at this time)
        return null;  // to please compiler
      }
    }

    Formal CloneFormal(Formal formal) {
      return new Formal(Tok(formal.tok), formal.Name, formal.Type, formal.InParam, formal.IsGhost);
    }

    BoundVar CloneBoundVar(BoundVar bv) {
      return new BoundVar(Tok(bv.tok), bv.Name, bv.Type);
    }

    Specification<Expression> CloneSpecExpr(Specification<Expression> spec) {
      var ee = spec.Expressions == null ? null : spec.Expressions.ConvertAll(CloneExpr);
      return new Specification<Expression>(ee, null);
    }

    Specification<FrameExpression> CloneSpecFrameExpr(Specification<FrameExpression> frame) {
      var ee = frame.Expressions == null ? null : frame.Expressions.ConvertAll(CloneFrameExpr);
      return new Specification<FrameExpression>(ee, null);
    }

    FrameExpression CloneFrameExpr(FrameExpression frame) {
      return new FrameExpression(CloneExpr(frame.E), frame.FieldName);
    }

    Attributes.Argument CloneAttrArg(Attributes.Argument aa) {
      if (aa.E != null) {
        return new Attributes.Argument(Tok(aa.Tok), CloneExpr(aa.E));
      } else {
        return new Attributes.Argument(Tok(aa.Tok), aa.S);
      }
    }

    MaybeFreeExpression CloneMayBeFreeExpr(MaybeFreeExpression expr) {
      return new MaybeFreeExpression(CloneExpr(expr.E), expr.IsFree);
    }

    Expression CloneExpr(Expression expr) {
      if (expr == null) {
        return null;
      } else if (expr is LiteralExpr) {
        var e = (LiteralExpr)expr;
        if (e.Value == null) {
          return new LiteralExpr(Tok(e.tok));
        } else if (e.Value is bool) {
          return new LiteralExpr(Tok(e.tok), (bool)e.Value);
        } else {
          return new LiteralExpr(Tok(e.tok), (BigInteger)e.Value);
        }

      } else if (expr is ThisExpr) {
        if (expr is ImplicitThisExpr) {
          return new ImplicitThisExpr(Tok(expr.tok));
        } else {
          return new ThisExpr(Tok(expr.tok));
        }

      } else if (expr is IdentifierExpr) {
        var e = (IdentifierExpr)expr;
        return new IdentifierExpr(Tok(e.tok), e.Name);

      } else if (expr is DatatypeValue) {
        var e = (DatatypeValue)expr;
        return new DatatypeValue(Tok(e.tok), e.DatatypeName, e.MemberName, e.Arguments.ConvertAll(CloneExpr));

      } else if (expr is DisplayExpression) {
        DisplayExpression e = (DisplayExpression)expr;
        if (expr is SetDisplayExpr) {
          return new SetDisplayExpr(Tok(e.tok), e.Elements.ConvertAll(CloneExpr));
        } else if (expr is MultiSetDisplayExpr) {
          return new MultiSetDisplayExpr(Tok(e.tok), e.Elements.ConvertAll(CloneExpr));
        } else {
          Contract.Assert(expr is SeqDisplayExpr);
          return new SeqDisplayExpr(Tok(e.tok), e.Elements.ConvertAll(CloneExpr));
        }

      } else if (expr is FieldSelectExpr) {
        var e = (FieldSelectExpr)expr;
        return new FieldSelectExpr(Tok(e.tok), CloneExpr(e.Obj), e.FieldName);

      } else if (expr is SeqSelectExpr) {
        var e = (SeqSelectExpr)expr;
        return new SeqSelectExpr(Tok(e.tok), e.SelectOne, CloneExpr(e.Seq), CloneExpr(e.E0), CloneExpr(e.E1));

      } else if (expr is MultiSelectExpr) {
        var e = (MultiSelectExpr)expr;
        return new MultiSelectExpr(Tok(e.tok), CloneExpr(e.Array), e.Indices.ConvertAll(CloneExpr));

      } else if (expr is SeqUpdateExpr) {
        var e = (SeqUpdateExpr)expr;
        return new SeqUpdateExpr(Tok(e.tok), CloneExpr(e.Seq), CloneExpr(e.Index), CloneExpr(e.Value));

      } else if (expr is FunctionCallExpr) {
        var e = (FunctionCallExpr)expr;
        return new FunctionCallExpr(Tok(e.tok), e.Name, CloneExpr(e.Receiver), e.Args.ConvertAll(CloneExpr));

      } else if (expr is OldExpr) {
        var e = (OldExpr)expr;
        return new OldExpr(Tok(e.tok), CloneExpr(e.E));

      } else if (expr is MultiSetFormingExpr) {
        var e = (MultiSetFormingExpr)expr;
        return new MultiSetFormingExpr(Tok(e.tok), CloneExpr(e.E));

      } else if (expr is FreshExpr) {
        var e = (FreshExpr)expr;
        return new FreshExpr(Tok(e.tok), CloneExpr(e.E));

      } else if (expr is AllocatedExpr) {
        var e = (AllocatedExpr)expr;
        return new AllocatedExpr(Tok(e.tok), CloneExpr(e.E));

      } else if (expr is UnaryExpr) {
        var e = (UnaryExpr)expr;
        return new UnaryExpr(Tok(e.tok), e.Op, CloneExpr(e.E));

      } else if (expr is BinaryExpr) {
        var e = (BinaryExpr)expr;
        return new BinaryExpr(Tok(e.tok), e.Op, CloneExpr(e.E0), CloneExpr(e.E1));

      } else if (expr is ChainingExpression) {
        var e = (ChainingExpression)expr;
        return CloneExpr(e.E);  // just clone the desugaring, since it's already available

      } else if (expr is LetExpr) {
        var e = (LetExpr)expr;
        return new LetExpr(Tok(e.tok), e.Vars.ConvertAll(CloneBoundVar), e.RHSs.ConvertAll(CloneExpr), CloneExpr(e.Body));

      } else if (expr is ComprehensionExpr) {
        var e = (ComprehensionExpr)expr;
        var tk = Tok(e.tok);
        var bvs = e.BoundVars.ConvertAll(CloneBoundVar);
        var range = CloneExpr(e.Range);
        var term = CloneExpr(e.Term);
        if (e is ForallExpr) {
          return new ForallExpr(tk, bvs, range, term, null);
        } else if (e is ExistsExpr) {
          return new ExistsExpr(tk, bvs, range, term, null);
        } else {
          Contract.Assert(e is SetComprehension);
          return new SetComprehension(tk, bvs, range, term);
        }

      } else if (expr is WildcardExpr) {
        return new WildcardExpr(Tok(expr.tok));

      } else if (expr is PredicateExpr) {
        var e = (PredicateExpr)expr;
        if (e is AssertExpr) {
          return new AssertExpr(Tok(e.tok), CloneExpr(e.Guard), CloneExpr(e.Body));
        } else {
          Contract.Assert(e is AssumeExpr);
          return new AssumeExpr(Tok(e.tok), CloneExpr(e.Guard), CloneExpr(e.Body));
        }

      } else if (expr is ITEExpr) {
        var e = (ITEExpr)expr;
        return new ITEExpr(Tok(e.tok), CloneExpr(e.Test), CloneExpr(e.Thn), CloneExpr(e.Els));

      } else if (expr is ParensExpression) {
        var e = (ParensExpression)expr;
        return CloneExpr(e.E);  // skip the parentheses in the clone

      } else if (expr is IdentifierSequence) {
        var e = (IdentifierSequence)expr;
        var aa = e.Arguments == null ? null : e.Arguments.ConvertAll(CloneExpr);
        return new IdentifierSequence(e.Tokens.ConvertAll(tk => Tok(tk)), Tok(e.OpenParen), aa);

      } else if (expr is MatchExpr) {
        var e = (MatchExpr)expr;
        return new MatchExpr(Tok(e.tok), CloneExpr(e.Source),
          e.Cases.ConvertAll(c => new MatchCaseExpr(Tok(c.tok), c.Id, c.Arguments.ConvertAll(CloneBoundVar), CloneExpr(c.Body))));

      } else {
        Contract.Assert(false); throw new cce.UnreachableException();  // unexpected expression
      }
    }

    AssignmentRhs CloneRHS(AssignmentRhs rhs) {
      if (rhs is ExprRhs) {
        var r = (ExprRhs)rhs;
        return new ExprRhs(r.Expr);
      } else if (rhs is HavocRhs) {
        return new HavocRhs(Tok(rhs.Tok));
      } else {
        var r = (TypeRhs)rhs;
        if (r.ArrayDimensions != null) {
          return new TypeRhs(Tok(r.Tok), r.EType, r.ArrayDimensions.ConvertAll(CloneExpr));
        } else if (r.InitCall != null) {
          return new TypeRhs(Tok(r.Tok), r.EType, (CallStmt)CloneStmt(r.InitCall));
        } else {
          return new TypeRhs(Tok(r.Tok), r.EType);
        }
      }
    }

    BlockStmt CloneBlockStmt(BlockStmt stmt) {
      if (stmt == null) {
        return null;
      } else {
        return new BlockStmt(Tok(stmt.Tok), stmt.Body.ConvertAll(CloneStmt));
      }
    }

    Statement CloneStmt(Statement stmt) {
      if (stmt == null) {
        return null;
      }

      Statement r;
      if (stmt is AssertStmt) {
        var s = (AssertStmt)stmt;
        r = new AssertStmt(Tok(s.Tok), s.Expr);

      } else if (stmt is AssumeStmt) {
        var s = (AssumeStmt)stmt;
        r = new AssumeStmt(Tok(s.Tok), s.Expr);

      } else if (stmt is PrintStmt) {
        var s = (PrintStmt)stmt;
        r = new PrintStmt(Tok(s.Tok), s.Args.ConvertAll(CloneAttrArg));

      } else if (stmt is BreakStmt) {
        var s = (BreakStmt)stmt;
        if (s.TargetLabel != null) {
          r = new BreakStmt(Tok(s.Tok), s.TargetLabel);
        } else {
          r = new BreakStmt(Tok(s.Tok), s.BreakCount);
        }

      } else if (stmt is ReturnStmt) {
        var s = (ReturnStmt)stmt;
        r = new ReturnStmt(Tok(s.Tok), s.rhss == null ? null : s.rhss.ConvertAll(CloneRHS));

      } else if (stmt is AssignStmt) {
        var s = (AssignStmt)stmt;
        r = new AssignStmt(Tok(s.Tok), CloneExpr(s.Lhs), CloneRHS(s.Rhs));

      } else if (stmt is VarDecl) {
        var s = (VarDecl)stmt;
        r = new VarDecl(Tok(s.Tok), s.Name, s.OptionalType, s.IsGhost);

      } else if (stmt is CallStmt) {
        var s = (CallStmt)stmt;
        r = new CallStmt(Tok(s.Tok), s.Lhs.ConvertAll(CloneExpr), CloneExpr(s.Receiver), s.MethodName, s.Args.ConvertAll(CloneExpr));

      } else if (stmt is BlockStmt) {
        r = CloneBlockStmt((BlockStmt)stmt);

      } else if (stmt is IfStmt) {
        var s = (IfStmt)stmt;
        r = new IfStmt(Tok(s.Tok), CloneExpr(s.Guard), CloneStmt(s.Thn), CloneStmt(s.Els));

      } else if (stmt is AlternativeStmt) {
        var s = (AlternativeStmt)stmt;
        r = new AlternativeStmt(Tok(s.Tok), s.Alternatives.ConvertAll(CloneGuardedAlternative));

      } else if (stmt is WhileStmt) {
        var s = (WhileStmt)stmt;
        r = new WhileStmt(Tok(s.Tok), CloneExpr(s.Guard), s.Invariants.ConvertAll(CloneMayBeFreeExpr), CloneSpecExpr(s.Decreases), CloneSpecFrameExpr(s.Mod), CloneStmt(s.Body));

      } else if (stmt is AlternativeLoopStmt) {
        var s = (AlternativeLoopStmt)stmt;
        r = new AlternativeLoopStmt(Tok(s.Tok), s.Invariants.ConvertAll(CloneMayBeFreeExpr), CloneSpecExpr(s.Decreases), CloneSpecFrameExpr(s.Mod), s.Alternatives.ConvertAll(CloneGuardedAlternative));

      } else if (stmt is ParallelStmt) {
        var s = (ParallelStmt)stmt;
        r = new ParallelStmt(Tok(s.Tok), s.BoundVars.ConvertAll(CloneBoundVar), null, CloneExpr(s.Range), s.Ens.ConvertAll(CloneMayBeFreeExpr), CloneStmt(s.Body));

      } else if (stmt is MatchStmt) {
        var s = (MatchStmt)stmt;
        r = new MatchStmt(Tok(s.Tok), CloneExpr(s.Source),
          s.Cases.ConvertAll(c => new MatchCaseStmt(Tok(c.tok), c.Id, c.Arguments.ConvertAll(CloneBoundVar), c.Body.ConvertAll(CloneStmt))));

      } else if (stmt is UpdateStmt) {
        var s = (UpdateStmt)stmt;
        r = new UpdateStmt(Tok(s.Tok), s.Lhss.ConvertAll(CloneExpr), s.Rhss.ConvertAll(CloneRHS), s.CanMutateKnownState);

      } else if (stmt is VarDeclStmt) {
        var s = (VarDeclStmt)stmt;
        r = new VarDeclStmt(Tok(s.Tok), s.Lhss.ConvertAll(c => (VarDecl)CloneStmt(c)), (UpdateStmt)CloneStmt(s.Update));

      } else {
        Contract.Assert(false); throw new cce.UnreachableException();  // unexpected statement
      }

      // add labels to the cloned statement
      AddStmtLabels(r, stmt.Labels);

      return r;
    }

    void AddStmtLabels(Statement s, LabelNode node) {
      if (node != null) {
        AddStmtLabels(s, node.Next);
        s.Labels = new LabelNode(Tok(node.Tok), node.Label, s.Labels);
      }
    }

    GuardedAlternative CloneGuardedAlternative(GuardedAlternative alt) {
      return new GuardedAlternative(Tok(alt.Tok), CloneExpr(alt.Guard), alt.Body.ConvertAll(CloneStmt));
    }

    // -------------------------------------------------- Merging ---------------------------------------------------------------

    DatatypeDecl MergeDatatype(DatatypeDecl nw, DatatypeDecl prev) {
      // TODO
      return nw;
    }

    ClassDecl MergeClass(ClassDecl nw, ClassDecl prev) {
      // TODO
      return nw;
    }
  }
}
