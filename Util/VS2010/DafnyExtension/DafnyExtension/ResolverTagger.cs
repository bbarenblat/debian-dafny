﻿//***************************************************************************
// Copyright © 2010 Microsoft Corporation.  All Rights Reserved.
// This code released under the terms of the 
// Microsoft Public License (MS-PL, http://opensource.org/licenses/ms-pl.html.)
//***************************************************************************
using EnvDTE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Text.Projection;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System.Diagnostics.Contracts;
using Dafny = Microsoft.Dafny;

namespace DafnyLanguage
{
  [Export(typeof(ITaggerProvider))]
  [ContentType("dafny")]
  [TagType(typeof(DafnyResolverTag))]
  internal sealed class ResolverTaggerProvider : ITaggerProvider
  {
    [Import(typeof(Microsoft.VisualStudio.Shell.SVsServiceProvider))]
    internal IServiceProvider _serviceProvider = null;

    [Import]
    ITextDocumentFactoryService _textDocumentFactory = null;

    public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag {
      // create a single tagger for each buffer.
      Func<ITagger<T>> sc = delegate() { return new ResolverTagger(buffer, _serviceProvider, _textDocumentFactory) as ITagger<T>; };
      return buffer.Properties.GetOrCreateSingletonProperty<ITagger<T>>(sc);
    }
  }

  public abstract class DafnyResolverTag : ITag
  {
  }
  public class DafnyErrorResolverTag : DafnyResolverTag
  {
    public readonly string Typ;
    public readonly string Msg;
    public DafnyErrorResolverTag(string typ, string msg) {
      Typ = typ;
      Msg = msg;
    }
  }
  public class DafnySuccessResolverTag : DafnyResolverTag
  {
    public readonly Dafny.Program Program;
    public DafnySuccessResolverTag(Dafny.Program program) {
      Program = program;
    }
  }

  /// <summary>
  /// Translate PkgDefTokenTags into ErrorTags and Error List items
  /// </summary>
  internal sealed class ResolverTagger : ITagger<DafnyResolverTag>, IDisposable
  {
    ITextBuffer _buffer;
    ITextDocument _document;
    public ITextSnapshot _snapshot;  // may be null
    List<DafnyError> _resolutionErrors = new List<DafnyError>();  // if nonempty, then _snapshot is the snapshot from which the errors were produced
    List<DafnyError> _verificationErrors = new List<DafnyError>();
    ErrorListProvider _errorProvider;
    public Dafny.Program _program;  // non-null only if the snapshot contains a Dafny program that type checks

    internal ResolverTagger(ITextBuffer buffer, IServiceProvider serviceProvider, ITextDocumentFactoryService textDocumentFactory) {
      _buffer = buffer;
      if (!textDocumentFactory.TryGetTextDocument(_buffer, out _document))
        _document = null;
      _snapshot = null;  // this makes sure the next snapshot will look different
      _errorProvider = new ErrorListProvider(serviceProvider);

      BufferIdleEventUtil.AddBufferIdleEventListener(_buffer, ProcessFile);
    }

    public void Dispose() {
      if (_errorProvider != null) {
        try {
          _errorProvider.Tasks.Clear();
        } catch (InvalidOperationException) {
          // this may occur if the SVsServiceProvider somehow has been uninstalled before our Dispose method is called
        }
        _errorProvider.Dispose();
      }
      BufferIdleEventUtil.RemoveBufferIdleEventListener(_buffer, ProcessFile);
    }

    public IEnumerable<DafnyError> AllErrors() {
      foreach (var err in _resolutionErrors) {
        yield return err;
      }
      foreach (var err in _verificationErrors) {
        yield return err;
      }
    }

    /// <summary>
    /// Find the Error tokens in the set of all tokens and create an ErrorTag for each
    /// </summary>
    public IEnumerable<ITagSpan<DafnyResolverTag>> GetTags(NormalizedSnapshotSpanCollection spans) {
      if (spans.Count == 0) yield break;
      foreach (var err in AllErrors()) {
        if (err.Category != ErrorCategory.ProcessError) {
          Contract.Assert(0 <= err.Line);
          var line = _snapshot.GetLineFromLineNumber(err.Line);
          Contract.Assert(err.Column <= line.Length);
          var length = line.Length - err.Column;
          if (5 < length) length = 5;
          var span = new SnapshotSpan(new SnapshotPoint(_snapshot, line.Start.Position + err.Column), length);
          string ty;  // the COLORs below indicate what I see on my machine
          switch (err.Category) {
            default:  // unexpected category
            case ErrorCategory.ParseError:
            case ErrorCategory.ParseWarning:
              ty = "syntax error"; break;  // COLOR: red
            case ErrorCategory.ResolveError:
              ty = "compiler error"; break;  // COLOR: blue
            case ErrorCategory.VerificationError:
              ty = "error"; break;  // COLOR: red
            case ErrorCategory.AuxInformation:
              ty = "other error"; break;  // COLOR: purple red
            case ErrorCategory.InternalError:
              ty = "error"; break;  // COLOR: red
          }
          yield return new TagSpan<DafnyResolverTag>(span, new DafnyErrorResolverTag(ty, err.Message));
        }
      }
      if (_program != null) {
        yield return new TagSpan<DafnyResolverTag>(new SnapshotSpan(_snapshot, 0, _snapshot.Length), new DafnySuccessResolverTag(_program));
      }
    }

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    /// <summary>
    /// Calls the Dafny verifier on the program, updates the Error List accordingly.
    /// </summary>
    void ProcessFile(object sender, EventArgs args) {
      ITextSnapshot snapshot = _buffer.CurrentSnapshot;
      if (snapshot == _snapshot)
        return;  // we've already done this snapshot
      NormalizedSnapshotSpanCollection spans = new NormalizedSnapshotSpanCollection(new SnapshotSpan(snapshot, 0, snapshot.Length));

      var driver = new DafnyDriver(_buffer.CurrentSnapshot.GetText(), _document != null ? _document.FilePath : "<program>");
      List<DafnyError> newErrors;
      Dafny.Program program;
      try {
        program = driver.Process();
        newErrors = driver.Errors;
      } catch (Exception e) {
        newErrors = new List<DafnyError>();
        newErrors.Add(new DafnyError(0, 0, ErrorCategory.InternalError, "internal Dafny error: " + e.Message));
        program = null;
      }

      _snapshot = snapshot;
      _program = program;
      PopulateErrorList(newErrors, false, snapshot);
    }

    public void PopulateErrorList(List<DafnyError> newErrors, bool verificationErrors, ITextSnapshot snapshot) {
      Contract.Requires(newErrors != null);
      foreach (var err in newErrors) {
        err.FillInSnapshot(snapshot);
      }
      if (verificationErrors) {
        _verificationErrors = newErrors;
      } else {
        _resolutionErrors = newErrors;
      }

      _errorProvider.SuspendRefresh();  // reduce flickering
      _errorProvider.Tasks.Clear();
      foreach (var err in AllErrors()) {
        ErrorTask task = new ErrorTask() {
          // CanDelete = true,
          Category = TaskCategory.BuildCompile,
          ErrorCategory = CategoryConversion(err.Category),
          Text = err.Message,
          Line = err.Line,
          Column = err.Column
        };
        if (_document != null) {
          task.Document = _document.FilePath;
        }
        if (err.Category != ErrorCategory.ProcessError && err.Category != ErrorCategory.InternalError) {
          task.Navigate += new EventHandler(NavigateHandler);
        }
        _errorProvider.Tasks.Add(task);
      }
      _errorProvider.ResumeRefresh();
      var chng = TagsChanged;
      if (chng != null)
        chng(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
    }

    TaskErrorCategory CategoryConversion(ErrorCategory cat) {
      switch (cat) {
        case ErrorCategory.ParseError:
        case ErrorCategory.ResolveError:
        case ErrorCategory.VerificationError:
        case ErrorCategory.InternalError:
          return TaskErrorCategory.Error;
        case ErrorCategory.ParseWarning:
          return TaskErrorCategory.Warning;
        case ErrorCategory.AuxInformation:
          return TaskErrorCategory.Message;
        default:
          Contract.Assert(false);  // unexpected category
          return TaskErrorCategory.Error;  // please compiler
      }
    }

    void NavigateHandler(object sender, EventArgs arguments) {
      var task = sender as ErrorTask;
      if (task == null || task.Document == null)
        return;

      // This would have been the simple way of doing things:
      //     _errorProvider.Navigate(error, new Guid(EnvDTE.Constants.vsViewKindCode));
      // Unfortunately, it doesn't work--it seems to ignore the column position.  (Moreover, it wants 1-based
      // line/column numbers, whereas the Error Task pane wants 0-based line/column numbers.)
      // So, instead we do all the things that follow:

      var openDoc = Package.GetGlobalService(typeof(IVsUIShellOpenDocument)) as IVsUIShellOpenDocument;
      if (openDoc == null)
        return;

      IVsWindowFrame frame;
      Microsoft.VisualStudio.OLE.Interop.IServiceProvider sp;
      IVsUIHierarchy hier;
      uint itemid;
      Guid logicalView = VSConstants.LOGVIEWID_Code;
      if (Microsoft.VisualStudio.ErrorHandler.Failed(openDoc.OpenDocumentViaProject(task.Document, ref logicalView, out sp, out hier, out itemid, out frame)) || frame == null)
        return;

      object docData;
      Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocData, out docData));

      // Get the VsTextBuffer
      VsTextBuffer buffer = docData as VsTextBuffer;
      if (buffer == null) {
        IVsTextBufferProvider bufferProvider = docData as IVsTextBufferProvider;
        if (bufferProvider != null) {
          IVsTextLines lines;
          Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(bufferProvider.GetTextBuffer(out lines));
          buffer = lines as VsTextBuffer;
          if (buffer == null)
            return;
        }
      }

      VsTextManager textManager = Package.GetGlobalService(typeof(VsTextManagerClass)) as VsTextManager;
      if (textManager == null)
        return;

      // Finally, move the cursor
      Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(textManager.NavigateToLineAndColumn(buffer, ref logicalView, task.Line, task.Column, task.Line, task.Column));
    }

  }

  public enum ErrorCategory
  {
    ProcessError, ParseWarning, ParseError, ResolveError, VerificationError, AuxInformation, InternalError
  }

  internal class DafnyError
  {
    public readonly int Line;  // 0 based
    public readonly int Column;  // 0 based
    public ITextSnapshot Snapshot;  // filled in during the FillInSnapshot call
    public readonly ErrorCategory Category;
    public readonly string Message;
    /// <summary>
    /// "line" and "col" are expected to be 0-based
    /// </summary>
    public DafnyError(int line, int col, ErrorCategory cat, string msg) {
      Line = line;
      Column = col;
      Category = cat;
      Message = msg;
    }

    public void FillInSnapshot(ITextSnapshot snapshot) {
      Contract.Requires(snapshot != null);
      Snapshot = snapshot;
    }
  }
}
