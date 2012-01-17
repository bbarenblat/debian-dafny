class C {
  var data: int;
  var n: nat;
  var st: set<object>;

  ghost method CLemma(k: int)
    requires k != -23;
    ensures data < k;  // magic, isn't it (or bogus, some would say)
}

// This method more or less just tests the syntax, resolution, and basic verification
method ParallelStatement_Resolve(
    a: array<int>,
    spine: set<C>,
    Repr: set<object>,
    S: set<int>,
    clx: C, cly: C, clk: int
  )
  requires a != null && null !in spine;
  modifies a, spine;
{
  parallel (i: int | 0 <= i < a.Length && i % 2 == 0) {
    a[i] := a[(i + 1) % a.Length] + 3;
  }

  parallel (o | o in spine) {
    o.st := o.st + Repr;
  }

  parallel (x, y | x in S && 0 <= y+x < 100) {
    Lemma(clx, x, y);  // error: precondition does not hold (clx may be null)
  }

  parallel (x, y | x in S && 0 <= y+x < 100) {
    cly.CLemma(x + y);  // error: receiver might be null
  }

  parallel (p | 0 <= p)
    ensures F(p) <= Sum(p) + p - 1;  // error (no connection is known between F and Sum)
  {
    assert 0 <= G(p);
    ghost var t;
    if (p % 2 == 0) {
      assert G(p) == F(p+2);  // error (there's nothing that gives any relation between F and G)
      t := p+p;
    } else {
      assume H(p, 20) < 100;  // don't know how to justify this
      t := p;
    }
    PowerLemma(p, t);
    t := t + 1;
    PowerLemma(p, t);
  }
}

ghost method Lemma(c: C, x: int, y: int)
  requires c != null;
  ensures c.data <= x+y;
ghost method PowerLemma(x: int, y: int)
  ensures Pred(x, y);

function F(x: int): int
function G(x: int): nat
function H(x: int, y: int): int
function Sum(x: int): int
function Pred(x: int, y: int): bool

// ---------------------------------------------------------------------

method M0(S: set<C>)
  requires null !in S;
  modifies S;
  ensures forall o :: o in S ==> o.data == 85;
  ensures forall o :: o != null && o !in S ==> o.data == old(o.data);
{
  parallel (s | s in S) {
    s.data := 85;
  }
}

method M1(S: set<C>, x: C)
  requires null !in S && x in S;
{
  parallel (s | s in S)
    ensures s.data < 100;
  {
    assume s.data == 85;
  }
  if (*) {
    assert x.data == 85;  // error (cannot be inferred from parallel ensures clause)
  } else {
    assert x.data < 120;
  }

  parallel (s | s in S)
    ensures s.data < 70;  // error
  {
    assume s.data == 85;
  }
}

method M2() returns (a: array<int>)
  ensures a != null;
  ensures forall i,j :: 0 <= i < a.Length/2 <= j < a.Length ==> a[i] < a[j];
{
  a := new int[250];
  parallel (i: nat | i < 125) {
    a[i] := 423;
  }
  parallel (i | 125 <= i < 250) {
    a[i] := 300 + i;
  }
}

method M4(S: set<C>, k: int)
  modifies S;
{
  parallel (s | s in S && s != null) {
    s.n := k;  // error: k might be negative
  }
}

method M5()
{
  if {
  case true =>
    parallel (x | 0 <= x < 100) {
      PowerLemma(x, x);
    }
    assert Pred(34, 34);

  case true =>
    parallel (x,y | 0 <= x < 100 && y == x+1) {
      PowerLemma(x, y);
    }
    assert Pred(34, 35);

  case true =>
    parallel (x,y | 0 <= x < y < 100) {
      PowerLemma(x, y);
    }
    assert Pred(34, 35);

  case true =>
    parallel (x | x in set k | 0 <= k < 100) {
      PowerLemma(x, x);
    }
    assert Pred(34, 34);
  }
}

method Main()
{
  var a := new int[180];
  parallel (i | 0 <= i < 180) {
    a[i] := 2*i + 100;
  }
  var sq := [0, 0, 0, 2, 2, 2, 5, 5, 5];
  parallel (i | 0 <= i < |sq|) {
    a[20+i] := sq[i];
  }
  parallel (t | t in sq) {
    a[t] := 1000;
  }
  parallel (t,u | t in sq && t < 4 && 10 <= u < 10+t) {
    a[u] := 6000 + t;
  }
  var k := 0;
  while (k < 180) {
    if (k != 0) { print ", "; }
    print a[k];
    k := k + 1;
  }
  print "\n";
}

method DuplicateUpdate() {
  var a := new int[180];
  var sq := [0, 0, 0, 2, 2, 2, 5, 5, 5];
  if (*) {
    parallel (t,u | t in sq && 10 <= u < 10+t) {
      a[u] := 6000 + t;  // error: a[10] (and a[11]) are assigned more than once
    }
  } else {
    parallel (t,u | t in sq && t < 4 && 10 <= u < 10+t) {
      a[u] := 6000 + t;  // with the 't < 4' conjunct in the line above, this is fine
    }
  }
}

ghost method DontDoMuch(x: int)
{
}

method OmittedRange() {
  parallel (x) { }
  parallel (x) {
    DontDoMuch(x);
  }
}

// ----------------------- two-state postconditions ---------------------------------

class TwoState_C { ghost var data: int; }

ghost static method TwoState0(y: int)
  ensures exists o: TwoState_C :: o != null && fresh(o);
{
  var p := new TwoState_C;
}

method TwoState_Main0() {
  parallel (x) { TwoState0(x); }
  assert false;  // error: this location is indeed reachable (if the translation before it is sound)
}

ghost static method TwoState1(y: int)
  ensures exists c: TwoState_C :: c != null && c.data != old(c.data);
{
  var c := new TwoState_C;
  c.data := c.data + 1;
}

method TwoState_Main1() {
  parallel (x) { TwoState1(x); }
  assert false;  // error: this location is indeed reachable (if the translation before it is sound)
}

method X_Legit(c: TwoState_C)
  requires c != null;
  modifies c;
{
  c.data := c.data + 1;
  parallel (x | c.data <= x)
    ensures old(c.data) < x;  // note that the 'old' refers to the method's initial state
  {
  }
}

// At first glance, this looks like a version of TwoState_Main0 above, but with an
// ensures clause.
// However, there's an important difference in the translation, which is that the
// occurrence of 'fresh' here refers to the initial state of the TwoStateMain2
// method, not the beginning of the 'parallel' statement.
// Still, care needs to be taken in the translation to make sure that the parallel
// statement's effect on the heap is not optimized away.
method TwoState_Main2()
{
  parallel (x: int)
    ensures exists o: TwoState_C :: o != null && fresh(o);
  {
    TwoState0(x);
  }
  assert false;  // error: this location is indeed reachable (if the translation before it is sound)
}

// At first glance, this looks like an inlined version of TwoState_Main0 above.
// However, there's an important difference in the translation, which is that the
// occurrence of 'fresh' here refers to the initial state of the TwoStateMain3
// method, not the beginning of the 'parallel' statement.
// Still, care needs to be taken in the translation to make sure that the parallel
// statement's effect on the heap is not optimized away.
method TwoState_Main3()
{
  parallel (x: int)
    ensures exists o: TwoState_C :: o != null && fresh(o);
  {
    var p := new TwoState_C;
  }
  assert false;  // error: this location is indeed reachable (if the translation before it is sound)
}
