// ----- Stream

codatatype Stream = Nil | Cons(head: int, tail: Stream);

function append(M: Stream, N: Stream): Stream
{
  match M
  case Nil => N
  case Cons(t, M') => Cons(t, append(M', N))
}

function zeros(): Stream
{
  Cons(0, zeros())
}

function ones(): Stream
{
  Cons(1, ones())
}

copredicate atmost(a: Stream, b: Stream)
{
  match a
  case Nil => true
  case Cons(h,t) => b.Cons? && h <= b.head && atmost(t, b.tail)
}

comethod Theorem0()
  ensures atmost(zeros(), ones());
{
  // the following shows two equivalent ways to getting essentially the
  // co-inductive hypothesis
  if (*) {
    Theorem0#[_k-1]();
  } else {
    Theorem0();
  }
}

ghost method Theorem0_Manual()
  ensures atmost(zeros(), ones());
{
  parallel (k: nat) {
    Theorem0_Lemma(k);
  }
  assume (forall k: nat :: atmost#[k](zeros(), ones())) ==> atmost(zeros(), ones());
}

datatype Natural = Zero | Succ(Natural);

comethod Theorem0_TerminationFailure_ExplicitDecreases(y: Natural)
  ensures atmost(zeros(), ones());
  decreases y;
{
  match (y) {
    case Succ(x) =>
      // this is just to show that the decreases clause does kick in
      Theorem0_TerminationFailure_ExplicitDecreases#[_k](x);
    case Zero =>
      Theorem0_TerminationFailure_ExplicitDecreases#[_k](y);  // error: failure to terminate
  }
  Theorem0_TerminationFailure_ExplicitDecreases#[_k-1](y);
}

comethod Theorem0_TerminationFailure_DefaultDecreases(y: Natural)
  ensures atmost(zeros(), ones());
{
  match (y) {
    case Succ(yy) =>
      // this is just to show that the decreases clause does kick in
      Theorem0_TerminationFailure_DefaultDecreases#[_k](yy);
    case Zero =>
      Theorem0_TerminationFailure_DefaultDecreases#[_k](y);  // error: failure to terminate
  }
  Theorem0_TerminationFailure_DefaultDecreases#[_k-1](y);
}

ghost method {:induction true} Theorem0_Lemma(k: nat)
  ensures atmost#[k](zeros(), ones());
{
}

comethod Theorem1()
  ensures append(zeros(), ones()) == zeros();
{
  Theorem1();
}

codatatype IList = ICons(head: int, tail: IList);

function UpIList(n: int): IList
{
  ICons(n, UpIList(n+1))
}

copredicate Pos(s: IList)
{
  s.head > 0 && Pos(s.tail)
}

comethod Theorem2(n: int)
  requires 1 <= n;
  ensures Pos(UpIList(n));
{
  Theorem2(n+1);
}

comethod Theorem2_NotAProof(n: int)
  requires 1 <= n;
  ensures Pos(UpIList(n));
{  // error: this is not a proof
}

codatatype TList<T> = TCons(head: T, tail: TList);

function Next<T>(t: T): T

function FF<T>(h: T): TList<T>
{
  TCons(h, FF(Next(h)))
}

function GG<T>(h: T): TList<T>
{
  TCons(h, GG(Next(h)))
}

comethod Compare<T>(h: T)
  ensures FF(h) == GG(h);
{
  assert FF(h).head == GG(h).head;
  Compare(Next(h));
  if {
    case true =>
      assert FF(h).tail == GG(h).tail;  // error: full equality is not known here
    case true =>
//      assert FF(h).tail == GG(h).tail;
    case true =>
//      assert FF(h).tail ==#[_k] GG(h).tail;
    case true =>
//      assert FF(h).tail ==#[_k - 1] GG(h).tail;
    case true =>
  }
}
