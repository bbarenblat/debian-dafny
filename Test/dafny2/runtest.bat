@echo off
setlocal

set BINARIES=..\..\Binaries
set DAFNY_EXE=%BINARIES\Dafny.exe

REM soon again:    SnapshotableTrees.dfy
for %%f in (
    Classics.dfy
    TreeBarrier.dfy
    COST-verif-comp-2011-1-MaxArray.dfy
    COST-verif-comp-2011-2-MaxTree-class.dfy
    COST-verif-comp-2011-2-MaxTree-datatype.dfy
    COST-verif-comp-2011-3-TwoDuplicates.dfy
    COST-verif-comp-2011-4-FloydCycleDetect.dfy
    StoreAndRetrieve.dfy
    Intervals.dfy TreeFill.dfy TuringFactorial.dfy
    MajorityVote.dfy SegmentSum.dfy
    MonotonicHeapstate.dfy Calculations.dfy
  ) do (
  echo.
  echo -------------------- %%f --------------------
  %DAFNY_EXE% /compile:0 /dprint:out.dfy.tmp %* %%f
)
