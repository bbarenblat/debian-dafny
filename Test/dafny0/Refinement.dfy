module A {
  class X { }
  class T {
    method M(x: int) returns (y: int)
      requires 0 <= x;
      ensures 0 <= y;
    {
      y := 2 * x;
    }
    method Q() returns (q: int, r: int, s: int)
      ensures 0 <= q && 0 <= r && 0 <= s;
    {  // error: failure to establish postcondition about q
      r, s := 100, 200;
    }
  }
}

module B refines A {
  class C { }
  datatype Dt = Ax | Bx;
  class T {
    method P() returns (p: int)
    {
      p := 18;
    }
    method M(x: int) returns (y: int)
      ensures y % 2 == 0;  // add a postcondition
    method Q() returns (q: int, r: int, s: int)
      ensures 12 <= r;
      ensures 1200 <= s;  // error: postcondition is not established by
                          // inherited method body
  }
}

// ------------------------------------------------

module A_AnonymousClass {
  var x: int;
  method Increment(d: int)
    modifies this;
  {
    x := x + d;
  }
}

module B_AnonymousClass refines A_AnonymousClass {
  method Increment(d: int)
    ensures x <= old(x) + d;
}

module C_AnonymousClass refines B_AnonymousClass {
  method Increment(d: int)
    ensures old(x) + d <= x;
  method Main()
    modifies this;
  {
    x := 25;
    Increment(30);
    assert x == 55;
    Increment(12);
    assert x == 66;  // error: it's 67
  }
}

// ------------------------------------------------

module BodyFree {
  function F(x: int): int
    ensures 0 <= F(x);
  method TestF() {
    assert F(6) == F(7);  // error: no information about F so far
  }
  method M() returns (a: int, b: int)
    ensures a == b;
}

module SomeBody refines BodyFree {
  function F(x: int): int
  { if x < 0 then 2 else 3 }
  method TestFAgain() {
    assert F(6) == F(7);
  }
  method M() returns (a: int, b: int)
  {
    a := b;  // good
  }
}

module FullBodied refines BodyFree {
  function F(x: int): int
  { x } // error: does not meet the inherited postcondition (note, confusing error-message location)
  method M() returns (a: int, b: int)
  {  // error: does not establish postcondition
    a := b + 1;
  }
}

// ------------------------------------------------
/*  SOON
module Abstract {
  class MyNumber {
    var N: int;
    ghost var Repr: set<object>;
    predicate Valid
      reads this, Repr;
    {
      this in Repr && null !in Repr
    }
    constructor Init()
      modifies this;
      ensures N == 0;
      ensures Valid && fresh(Repr - {this});
    {
      N, Repr := 0, {this};
    }
    method Inc()
      requires Valid;
      modifies Repr;
      ensures N == old(N) + 1;
      ensures Valid && fresh(Repr - old(Repr));
    {
      N := N + 1;
    }
    method Get() returns (n: int)
      requires Valid;
      ensures n == N;
    {
      n := N;
    }
  }
}

module Concrete refines Abstract {
  class MyNumber {
    var a: int;
    var b: int;
    predicate Valid
    {
      N == a - b
    }
    constructor Init()
    {
      a := b;
    }
    method Inc()
    {
      if (*) { a := a + 1; } else { b := b - 1; }
    }
    method Get() returns (n: int)
    {
      n := a - b;
    }
  }
}

module Client imports Concrete {
  class TheClient {
    method Main() {
      var n := new MyNumber.Init();
      n.Inc();
      n.Inc();
      var k := n.Get();
      assert k == 2;
    }
  }
}
*/
