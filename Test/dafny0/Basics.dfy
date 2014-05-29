// RUN: %dafny /compile:0 /print:"%t.print" /dprint:"%t.dprint" "%s" > "%t"
// RUN: %diff "%s.expect" "%t"

class Global {
  static function G(x: int): int { x+x }
  static method N(ghost x: int) returns (ghost r: int)
    ensures r == Global.G(x);
  {
    if {
      case true =>  r := G(x+0);
      case true =>
        var g: Global;
        r := g.G(x);
      case true =>
        var g: Global := null;
        r := g.G(x);
      case true =>
        r := Global.G(x);
    }
  }
}

method TestCalls(k: nat) {
  var g: Global, h: Global;
  assume g != h;
  ghost var r: int;
  ghost var s := Global.G(k);

  r := Global.N(k);
  assert r == s;

  r := g.N(k);
  assert r == s;
  r := h.N(k);
  assert r == s;

  g := null;
  r := g.N(k);
  assert r == s;

  r := Global.N(r);
  if (k == 0) {
    assert r == s;
  } else {
    assert r == s;  // error: G(k) and G(k+k) are different
  }
}

// ---------- chaining operators ------------------------------------

function UpTruth(j: int, k: int): bool
  requires 10 <= j < 180 < 220 <= k;
{
  0 < 2 <= 2 < j != 200 < k < k + 1
}

function DownTruth(j: int, k: int): bool
  requires k >= 220 > 180 > j >= 10;
{
  k + 1 > k > 200 != j > 2 >= 2 > 0
}

method ChallengeTruth(j: int, k: int)
  requires 80 <= j < 150 && 250 <= k < 1000;
{
  assert UpTruth(j, k);
  assert DownTruth(j, k);
  // but this is not equally true:
  assert j <= j + k != k + j + 1 < k+k+j <=/*this is the error*/ j+j+k < k+k+j+j == 2*k + 2*j == 2*(k+j);
}

// ---------- reverse implication ------------------------------------

method Explies(s: seq<int>, i: nat)
  requires forall x :: x in s ==> x > 0;
{
  var a, b, c: bool;
  assert a <== b <== c <== false;   // OK, because <== is left-associative
  assert s[i] > 0 <== i < |s|;      // OK, because <== is short-circuiting from the right
}

// --------- multi assignments --------------------------------

class Multi {
  var x: int;
  var y: int;
  var next: Multi;
  method Mutate(z: int) returns (m: Multi)
    requires 0 <= z;
    modifies this;
    ensures y == old(y);
  {
    x := x + z;
  }
  method IncX() returns (oldX: int)
    modifies this;
    ensures x == old(x) + 1 && oldX == old(x);
  {
    x, oldX := x + 1, x;
  }
}

method TestMulti(m: Multi, p: Multi)
  requires m != null && p != null;
  modifies m, p;
{
  m.x := 10;
  m.y := 12;
  p.x := 20;
  p.y := 22;
  if (*) {
    assert p.x == 20;
    assert m.x == 10;  // error: m and p may be the same
  }
  var t, u;
  u, m.x, t := 100, u + t + m.x, 200;
  m.x := 0;
  u, m.x, t := 200, u + t + m.x, 400;
  assert m.x == 300;
  if (p.x != 300) {
    p.x, m.x := m.x, p.x;
  }
  assert p.x == 300;
  if (*) {
    p.x, m.y := 10, 10;
    p.x, m.x := 8, 8;
  }

  var a, b := new int[20], new int[30];
  a[4], b[10], a[0], a[3], b[18] := 0, 1, 2, 3, 4;
  a[4], b[b[18]] := 271, 272;
  a[4], a[b[18]] := 273, 274;  // error: duplicate assignment (since b[18] is 4)
}

class MyBoxyClass<T> {
  var f: T;
}

method TestBoxAssignment<T>(x: MyBoxyClass<int>, y: MyBoxyClass<T>, t: T)
  requires x != null && y != null;
  modifies x, y;
{
  y.f := t;
  x.f := 15;
  // all together now:
  y.f, x.f := t, 15;  // error: duplicate assignment (if T==int and x==y)
  var k: int := x.f;
}

method TestCallsWithFancyLhss(m: Multi)
  requires m != null && m.next != null;
  modifies m, m.next;
{
  m.x := 10;
  var p := m.next;
  m.next.next := m.Mutate(m.x);  // fine
  if (*) {
    assert m.next == old(m.next);  // error: the call to Mutate may have changed m.next
  }
  m.next.next := m.Mutate(20);  // error: LHS may not be well defined (m.next may be null)
  m.x, m.next := 12, p;
  m.x, m.y := SwapEm(m.x, m.y);
  assert m.y == 12;
  if (*) {
    m.x, m.x := SwapEm(m.x, m.y);  // error: duplicate among LHSs
  }
  m.x := 30;
  var xx := m.IncX();
  assert xx == 30;
  m.y := m.IncX();
  assert m.y == 31 && m.x == 32;
  m.x := m.IncX();
  assert m.x == 32;
  xx := m.IncX();
  if (*) {
    assert xx == 33;  // error: xx will in fact be 32
  } else {
    assert xx == 32;  // see!
  }
}

method SwapEm(a: int, b: int) returns (x: int, y: int)
  ensures x == b && y == a;
{
  x, y := b, a;
}

function method abs(a:int): int
{
   if a <= 0 then -a else a
}
// test of verifier using euclidean division.
method EuclideanTest(a: int, b: int)
   requires b != 0;
{
   var q, r := a / b, a % b;
   assert 0 <= r < abs(b);
   assert a == b * q + r;
   assert (a/b) * b + a % b == a;
}

method havocInMultiassignment()
{
   var i: nat, j: nat;
   i, j := *, 3;
   assert 0 <= i;
}

method m()
{
  var i: int, j: int;
  i, j := 3, 6;
}

method swap(a: array<int>, i: nat, j: nat)
  requires a != null && 0 <= i < a.Length && 0 <= j < a.Length;
  modifies a;
{
  a[i], a[j] := a[j], a[i];
}

class CC {
  var x : int;
  var y : int;
}

method notQuiteSwap(c: CC, d: CC)
  requires c != null && d != null;
  modifies c,d;
{
  c.x, d.x := c.x, c.x;
}

method notQuiteSwap2(c: CC, d: CC)
  requires c != null && d != null;
  modifies c,d;
{
  c.x, d.x := d.x, c.y; // BAD: c and d could be the same.
}

method OKNowIt'sSwapAgain(c: CC, d: CC)
  requires c != null && d != null;
  modifies c,d;
{
  c.x, d.x := d.x, c.x;
}

method notQuiteSwap3(c: CC, d: CC)
  requires c != null && d != null && c != d;
  modifies c,d;
{
  c.x, d.x := 4, c.y;
  c.x, c.y := 3, c.y;
}

// ---------------------------
// regression tests of things that were once errors

method InlineMultisetFormingExpr(s: seq<int>)
  ensures MSFE(s);
predicate MSFE(s: seq<int>)
{
  multiset(s) == multiset(s)
}

copredicate CoPredTypeCheck(n: int)
  requires n != 0;

// -------------------- set cardinality ----------------------------------

module SetCardinality {
  method A(s: set<int>)
    requires s != {};
  {
    if {
      case true =>  assert s != {};
      case true =>  assert |s| != 0;
      case true =>  assert exists x :: x in s;
      case true =>  var y :| y in s;
    }
  }

  method B(s: set<int>)
    requires |s| != 0;
  {
    if {
      case true =>  assert s != {};
      case true =>  assert |s| != 0;
      case true =>  assert exists x :: x in s;
      case true =>  var y :| y in s;
    }
  }

  method C(s: set<int>)
    requires exists x :: x in s;
  {
    if {
      case true =>  assert s != {};
      case true =>  assert |s| != 0;
      case true =>  assert exists x :: x in s;
      case true =>  var y :| y in s;
    }
  }

  method A'(s: set<int>)
    requires s == {};
  {
    if {
      case true =>  assert s == {};
      case true =>  assert |s| == 0;
      case true =>  assert !exists x :: x in s;
      case true =>  var y :| y !in s;
    }
  }

  method B'(s: set<int>)
    requires |s| == 0;
  {
    if {
      case true =>  assert s == {};
      case true =>  assert |s| == 0;
      case true =>  assert !exists x :: x in s;
      case true =>  var y :| y !in s;
    }
  }

  method C'(s: set<int>)
    requires forall x :: x !in s;
  {
    if {
      case true =>  assert s == {};
      case true =>  assert |s| == 0;
      case true =>  assert !exists x :: x in s;
      case true =>  var y :| y !in s;
    }
  }

  method LetSuchThatExpression(s: set<int>)
    ensures |s| != 0 ==> var x :| x in s; true;
  {
  }

  method G<T>(s: set<T>, t: set<T>)
    requires s <= t;
    ensures |s| <= |t|;  // it doesn't get this immediately, but the method body offers different proofs
  {
    if {
      case true =>  assert |t - s| + |t * s| == |t|;
      case true =>  calc {
                      |s| <= |t|;
                    <==
                      |s - s| <= |t - s|;
                    }
      case true =>  assert 0 <= |t - s|;
    }
  }

  method H(s: multiset<int>, t: multiset<int>)
    requires s <= t;
    ensures |s| <= |t|;  // it doesn't get this immediately, but the method body offers different proofs
  {
    if {
      case true =>  assert |t - s| + |t * s| == |t|;
      case true =>  calc {
                      |s| <= |t|;
                    <==
                      |s - s| <= |t - s|;
                    }
      case true =>  assert 0 <= |t - s|;
    }
  }

  method K<T>(s: multiset<T>, t: multiset<T>)
  {
    assert |s * t|    +    |t * s|
                      +
              |s - t| + |t - s|
                     ==
                   |s + t|;
  }
}


// -------------------- hex support ----------------------------------

method HexTest()
{
  var first16lower := [ 0x0, 0x1, 0x2, 0x3, 0x4, 0x5, 0x6, 0x7, 0x8, 0x9, 0xa, 0xb, 0xc, 0xd, 0xe, 0xf ];
  var first16upper := [ 0x0, 0x1, 0x2, 0x3, 0x4, 0x5, 0x6, 0x7, 0x8, 0x9, 0xA, 0xB, 0xC, 0xD, 0xE, 0xF ];
  assert forall i :: 0 <= i < |first16lower| ==> first16lower[i] == i;
  assert forall i :: 0 <= i < |first16upper| ==> first16upper[i] == i;

  var randomHex := [ 0xCF4458F2, 0x9A5C5BAF, 0x26A6ABD6, 0x00EB3933, 0x9123D2CF, 0xBE040001, 0x5AD038FA, 0xC75597A6, 0x7CF821A7, 0xCDEFB617, 0x4889D968, 0xB05F896A,
             0x75B18DB2, 0xCAD773B0, 0xD8845EF8, 0x8EFA513D, 0x8EBAD321, 0x9C405DDE, 0x0EA9DF16, 0xCD48236A, 0x8A6892CF, 0x99BF0779, 0xEA52E108, 0x0379BF46, 
             0x610D0339, 0xDB433BC7, 0xE94C026E, 0xFC18735C, 0x6A5FBDB3, 0xFDA622F9, 0x6204DB79, 0x52050F94, 0x5ABDD3FD, 0x7F1CFCDF, 0xEC7C907F, 0xFA41A43D, 
             0x02FBF254, 0x9E76751A, 0xF753002C, 0x9635D361, 0xBA2C14E6, 0x415CA2FB, 0xA478EF6C, 0x7F80D7EC, 0xB4DD8598, 0x06C4ED20, 0xBFC9F800, 0x69F9675D, 
             0x730D85E7, 0x30152491, 0x0226b79d, 0x6c7f895c, 0x4f466fa2, 0x2e470749, 0xacacf22e, 0x455ab875, 0xa0927dc7, 0xe27c93d7, 0x4f134daf, 0xd2c6c190, 
             0xc95f056e, 0x45547ddf, 0x6a5c2767, 0xadc55905, 0xc5d6217d, 0x4ae4453e, 0xbe11d3d9, 0x339b8b14, 0xe68f7571, 0xf528199d, 0x0e640ee0, 0x9f716143, 
             0x1520b76f, 0x7bfe38e9, 0x8c289b71, 0x677ff535, 0x0bb94f4e, 0xfb417c00, 0xa1cac03a, 0x5e3cdaf2, 0x7616f734, 0xb55744fb, 0x27642f2b, 0xa161c47e, 
             0xbfcd3fff, 0xa62df579, 0x3ea317b0, 0xc87063bf, 0x0038c98d, 0x95a5e874, 0x76d824f6, 0x18687e3e, 0x4be6d02a, 0x2c2cc14c, 0x6e91d56b, 0x76e2bb30, 
             0xcd85f1cc, 0x6219d3ae, 0xbe59d8d4, 0xd8C6FAF7 ];
  var randomDec := [ 3477362930, 2589744047, 648457174, 15415603, 2435044047, 3187933185, 1523595514, 3344275366, 2096636327, 3455038999, 1216993640, 2959051114, 
             1974570418, 3403117488, 3632553720, 2398769469, 2394608417, 2621464030, 246013718, 3444056938, 2322109135, 2579433337, 3931300104, 58310470, 
             1628242745, 3678616519, 3914072686, 4229460828, 1784659379, 4255523577, 1644485497, 1376063380, 1522390013, 2132606175, 3967586431, 4198605885, 
             50066004, 2658563354, 4149411884, 2520109921, 3123451110, 1096590075, 2759389036, 2139150316, 3034416536, 113569056, 3217684480, 1777952605, 
             1930266087, 806691985, 36091805, 1820297564, 1330016162, 776406857, 2897015342, 1163573365, 2693955015, 3799815127, 1326665135, 3536241040,
             3378447726, 1163165151, 1784424295, 2915391749, 3319144829, 1256473918, 3188839385, 865831700, 3868161393, 4113045917, 241438432, 2675007811,
             354465647, 2080258281, 2351471473, 1736439093, 196693838, 4215372800, 2714419258, 1581046514, 1981216564, 3042395387, 660877099, 2707539070,
             3217899519, 2788029817, 1050875824, 3362808767,  3721613, 2510678132, 1993876726, 409501246, 1273417770, 741130572, 1855051115, 1994570544,
             3448107468, 1645859758, 3193559252, 3636919031 ];
  assert forall i :: 0 <= i < |randomHex| ==> randomHex[i] == randomDec[i];
}

// ------------------------ attributes on assert/assume and methods are passed down -----

// To test this in Dafny, we're using Boogie's :selective_checking and :start_checking_here
// attributes.  This also tests the useful mode of using these attributes in Dafny programs.
method {:selective_checking} MySelectiveChecking0(x: int, y: int, z: int)
{
  if * {
    // this is another branch
    assert y + 129 == z;  // no complaint, since we're using :selective_checking
  } else {
    assert x < y;  // no complaint
    assert y < z;  // ditto
    assume {:start_checking_here} true;
    assert x < z;  // this holds, so there's no complaint here, either
  }
  assert x < z;  // error (this doesn't hold if we take the 'then' branch)
}
method {:selective_checking} MySelectiveChecking1(x: int, y: int, z: int)
{
  if * {
    // this is another branch
    assert y + 129 == z;  // no complaint, since we're using :selective_checking
  } else {
    assert x < y;  // no complaint
    assert y < z;  // ditto
    assert {:start_checking_here} true;
    assert x + 10 < z;  // error
  }
  assert x < z;  // error (this doesn't hold if we take the 'then' branch)
}
