method R1(ghost x: real, ghost y: real, i: int) {
  assert x + y == y + x;
  assert int(real(i)) == i;
  assert real(int(x)) <= x;
  assert real(int(x)) >= x; // error
}

method R2(ghost x: real, ghost y: real) {
  assert x * x >= real(0);
  assert y != real(0) ==> x / y * y == x;
  assert x / y * y == x; // error(s)
}

// Check that literals are handled properly
method R3() {
  ghost var x := 1.5;
  ghost var y := real(3);
  assert x == y / 2.0000000;  
  assert x == y / 2.000000000000000000000000001;  // error
}

// Check that real value in decreases clause doesn't scare Dafny
function R4(x:int, r:real) : int
{
	if x < 0 then 5
	else R4(x - 1, r)
}
