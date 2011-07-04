﻿/// Various utility functions
///
/// author: Aleksandar Milicevic (t-alekm@microsoft.com)

module Utils

// -------------------------------------------
// ----------- collection util funcs ---------
// -------------------------------------------

/// requres: x = Some(a)
/// ensures: ret = a
let ExtractOption x = 
  match x with
  | Some(a) -> a
  | None -> failwith "can't extract anything from a None"

/// requres: List.length lst <= 1
/// ensures: if |lst| = 0 then
///            ret = None
///          else
///            ret = Some(lst[0])
let ListToOption lst = 
  if List.length lst > 1 then
    failwith "given list contains more than one element"
  if List.isEmpty lst then
    None
  else
    Some(lst.[0])

/// requres: Seq.length seq <= 1
/// ensures: if |seq| = 0 then
///            ret = None
///          else
///            ret = Some(seq[0])
let SeqToOption seq = 
  if Seq.length seq > 1 then
    failwith "given seq contains more than one element"
  if Seq.isEmpty seq then
    None
  else
    Some(Seq.nth 0 seq)

/// requires: Set.count set <= 1
/// ensures: if |set| = 0 then
///            ret = None
///          else
///            ret = Some(set[0])
let SetToOption set = 
  if Set.count set > 1 then
    failwith "give set contains more than one value"
  if (Set.isEmpty set) then
    None
  else 
    Some(set |> Set.toList |> List.head)

/// requires: n >= 0
/// ensures:  |ret| = n && forall i :: 0 <= i < n ==> ret[i] = None
let rec GenList n =
  if n < 0 then 
    failwith "n must be positive"
  if n = 0 then
    []
  else
    None :: (GenList (n-1))

/// ensures: ret = elem in lst
let ListContains elem lst = 
  lst |> List.exists (fun e -> e = elem)

/// ensures: |ret| = max(|lst| - cnt, 0)
/// ensures: forall i :: cnt <= i < |lst| ==> ret[i] = lst[i-cnt]
let rec ListSkip cnt lst = 
  if cnt = 0 then
    lst    
  else
    match lst with
    | fs :: rest -> ListSkip (cnt-1) rest
    | [] -> []

/// ensures: forall i :: 0 <= i < max(|srcList|, |dstList|) ==> 
///            if i = idx then
///              ret[i] = v
///            elif i < |srcList| then
///              ret[i] = srcList[i]
///            else
///              ret[i] = dstList[i] 
let rec ListBuild srcList idx v dstList =
  match srcList, dstList with
  | fs1 :: rest1, fs2 :: rest2 -> if idx = 0 then
                                    v :: List.concat [rest1 ; ListSkip (List.length rest1) rest2]
                                  else 
                                    fs1 :: ListBuild rest1 (idx-1) v rest2
  | [],           fs2 :: rest2 -> if idx = 0 then
                                    v :: rest2
                                  else 
                                    fs2 :: ListBuild [] (idx-1) v rest2
  | _,            []           -> failwith "index out of range"

/// ensures: forall i :: 0 <= i < |lst| ==>
///            if i = idx then
///              ret[i] = v
///            else
///              ret[i] = lst[i]
let rec ListSet idx v lst =
  match lst with
  | fs :: rest -> if idx = 0 then 
                    v :: rest
                  else
                    fs :: ListSet (idx-1) v rest
  | [] -> failwith "index out of range"

// -------------------------------------------
// ------ string active patterns -------------
// -------------------------------------------

let (|Prefix|_|) (p:string) (s:string) =
  if s.StartsWith(p) then
    Some(s.Substring(p.Length))
  else
    None

let (|Exact|_|) (p:string) (s:string) =
  if s = p then
    Some(s)
  else
    None

