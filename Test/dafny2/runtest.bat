@echo off
setlocal

set BOOGIEDIR=..\..\Binaries
set DAFNY_EXE=%BOOGIEDIR%\Dafny.exe
set CSC=c:/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe

for %%f in (
    Classics.dfy
    SnapshotableTrees.dfy TreeBarrier.dfy
    COST-verif-comp-2011-1-MaxArray.dfy
    COST-verif-comp-2011-2-MaxTree-class.dfy
    COST-verif-comp-2011-2-MaxTree-datatype.dfy
    COST-verif-comp-2011-3-TwoDuplicates.dfy
    COST-verif-comp-2011-4-FloydCycleDetect.dfy
  ) do (
  echo.
  echo -------------------- %%f --------------------
  %DAFNY_EXE% /compile:0 /dprint:out.dfy.tmp %* %%f
)
