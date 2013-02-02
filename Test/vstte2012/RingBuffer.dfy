class RingBuffer<T>
{
  // public fields that are used to define the abstraction:
  ghost var Contents: seq<T>;  // the contents of the ring buffer
  ghost var N: nat;  // the capacity of the ring buffer
  ghost var Repr: set<object>;  // the set of objects used in the implementation

  // Valid encodes the consistency of RingBuffer objects (think, invariant).
  // An explanation of this idiom is explained in the README file.
  function Valid(): bool
    reads this, Repr;
  {
    this in Repr && null !in Repr &&
    data != null && data in Repr &&
    data.Length == N &&
    (N == 0 ==> len == start == 0 && Contents == []) &&
    (N != 0 ==> len <= N && start < N) &&
    Contents == if start + len <= N then data[start..start+len] 
                                    else data[start..] + data[..start+len-N]
  }

  // private implementation:
  var data: array<T>;
  var start: nat;
  var len: nat;

  constructor Create(n: nat)
    modifies this;
    ensures Valid() && fresh(Repr - {this});
    ensures Contents == [] && N == n;
  {
    Repr := {this};
    data := new T[n];
    Repr := Repr + {data};
    start, len := 0, 0;
    Contents, N := [], n;
  }

  method Clear()
    requires Valid();
    modifies Repr;
    ensures Valid() && fresh(Repr - old(Repr));
    ensures Contents == [] && N == old(N);
  {
    len := 0;
    Contents := [];
  }

  method Head() returns (x: T)
    requires Valid();
    requires Contents != [];
    ensures x == Contents[0];
  {
    x := data[start];
  }

  method Push(x: T)
    requires Valid();
    requires |Contents| != N;
    modifies Repr;
    ensures Valid() && fresh(Repr - old(Repr));
    ensures Contents == old(Contents) + [x] && N == old(N);
  {
    var nextEmpty := if start + len < data.Length 
                     then start + len else start + len - data.Length;
    data[nextEmpty] := x;
    len := len + 1;
    Contents := Contents + [x];
  }

  method Pop() returns (x: T)
    requires Valid();
    requires Contents != [];
    modifies Repr;
    ensures Valid() && fresh(Repr - old(Repr));
    ensures x == old(Contents)[0] && Contents == old(Contents)[1..] && N == old(N);
  {
    x := data[start];
    start, len := if start + 1 == data.Length then 0 else start + 1, len - 1;
    Contents := Contents[1..];
  }
}

method TestHarness(x: int, y: int, z: int)
{
  var b := new RingBuffer<int>.Create(2);
  b.Push(x);
  b.Push(y);
  var h := b.Pop();  assert h == x;
  b.Push(z);
  h := b.Pop();  assert h == y;
  h := b.Pop();  assert h == z;
}
