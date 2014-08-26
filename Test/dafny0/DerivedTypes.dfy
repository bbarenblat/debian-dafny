// RUN: %dafny /print:"%t.print" /dprint:"%t.dprint" "%s" > "%t"
// RUN: %diff "%s.expect" "%t"

newtype int32 = int
newtype posReal = real
newtype int8 = int32

method M()
{
  var k8 := new int8[100];
  var s: set<int32>;
  var x: posReal;
  var y: posReal;
  var yOrig := y;
  var z: int32;
  x := 5.3;
  z := 12;
  s := {};
  s := {40,20};
  x := x + y;
  var r0 := real(x);
  var r1: real := 2.0 * r0;
  var i0 := int(z);
  var i1: nat := 2 * i0;
  assert i1 == 24;
  assert y == 0.0 ==> r1 == 10.6;

  assert real(x) == r0;
  assert 2.0 * real(x) == real(2.0 * x);

  assert real(int(z)) == real(i0);
  assert 2 * int(z) == int(2 * z);

  var di: int32 := z / 2 + 24 / z;
  assert di == 8;
  y := 60.0;
  var dr: posReal := y / 2.0 + 120.0 / y;
  assert dr == 32.0;

  if yOrig == 0.3 {
    var truncated := r0.Trunc + x.Trunc;
    assert truncated == 5 + 5;
    var rounded := (r0 + 0.5).Trunc;
    assert rounded == 6;
  }
}

module Constraints {
  newtype SmallInt = x: int | 0 <= x < 100
  newtype LargeInt = y: int | 0 <= y < 100

  newtype A = x: int | 0 <= x
  newtype B = x: A | x < 100
  newtype C = B  // the constraints 0 <= x < 100 still apply

  static predicate IsEven(x: int)  // note that this is a ghost predicate
  {
    x % 2 == 0
  }
  newtype G = x: int | IsEven(x)  // it's okay to use ghost constructs in type constraints

  newtype N = nat

  newtype AssertType = s: int |
    var k := s;
    assert k <= s;
    k < 10 || 10 <= s

  newtype Te = x: int | 0 <= x < 3 && [5, 7, 8][x] % 2 != 0

  newtype Ta = x: int | 0 <= x < 3
  newtype Tb = y: Ta | [5, 7, 8][int(y)] % 2 != 0  // the indexing is okay, because of the type constraint for Ta

  newtype Odds = x: int | x % 2 == 1  // error: cannot find witness

  newtype K = x: real | 10.0 <= x ==> 200.0 / (x - 20.0) < 30.0  // error: division by zero
}

module PredicateTests {
  newtype char8 = x: int | 0 <= x < 256

  method M() {
    var u: char8 := 85;
    var v: char8 := 86;
    var ch := u + v - v + u;
    assert ch + u == 255;
    ch := ch + v - 3;  // error: value out of range (for the plus operation)
  }

  method N() {
    var y: char8;
    if * {
      y := y / 2;
      y := y + 1;
      y := 300;  // error: value out of range
    } else {
      y := y + 1;  // error: value out of range
    }
  }

  method MidPoint_Bad(lo: char8, hi: char8) returns (mid: char8)
    requires lo <= hi;
  {
    mid := (lo + hi) / 2;  // error: intermediate result is out of range
  }

  method MidPoint_Good(lo: char8, hi: char8) returns (mid: char8)
    requires lo <= hi;
  {
    mid := lo + (hi - lo) / 2;
  }

  method MidPoint_AlsoFine(lo: char8, hi: char8) returns (mid: char8)
    requires lo <= hi;
  {
    mid := char8((int(lo) + int(hi)) / 2);
  }
}

module Module0 {
  import Module1
  method M(x: int) returns (n: Module1.N9) {
    n := Module1.N9(x);
  }
}

module Module1 {
  newtype N9 = int
}

module DatatypeCtorResolution {
  datatype Pair = Pair(int, int)

  method M() {
    var p := Pair(5, 6);
    var q: Pair;
    q := p;
    q := Pair.Pair(10, 20);
  }
}

module X {
  newtype Int = x | 0 <= x < 100
  newtype Real = r | 0.0 <= r <= 100.0

  method M() returns (i: Int, r: Real)
  {
    i := 4;
    r := 4.0;
  }

  method N()
  {
    var x := var i := 3; i;
    var y := var j := 3.0; j;
  }
}
