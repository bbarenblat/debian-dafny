﻿module FixpointSolver

open Ast
open AstUtils
open Resolver
open Utils

/////////////

type UnifDirection = LTR | RTL

exception CannotUnify

let rec SelectiveUnifyImplies okToUnifyFunc lhs rhs dir unifs = 
  ///
  let __AddOrNone unifs name e = 
    if okToUnifyFunc name then
      Some(unifs |> Utils.MapAddNew name e)
    else
      None
  
  ///
  let __UnifLists lstL lstR = 
    if List.length lstL = List.length lstR then
      try 
        let unifs2 = List.fold2 (fun acc elL elR -> match SelectiveUnifyImplies okToUnifyFunc elL elR dir acc with
                                                    | Some(u) -> u
                                                    | None -> raise CannotUnify) unifs lstL lstR
        Some(unifs2)
      with 
      | CannotUnify -> None
    else
      None
  
  ///
  let __ApplyUnifs unifs exprList = 
            exprList |> List.fold (fun acc e -> 
                                     let e' = e |> Rewrite (fun e ->
                                                              match e with 
                                                              | VarLiteral(id) when Map.containsKey id unifs -> Some(unifs |> Map.find id)
                                                              | _ -> None)
                                     acc |> Set.add e'
                                  ) Set.empty

  if lhs = FalseLiteral || rhs = TrueLiteral then
    Some(unifs)
  else 
    try 
      let l,r = match dir with
                | LTR -> lhs,rhs
                | RTL -> rhs,lhs
      match l, r with
      | VarLiteral(vname), rhs -> __AddOrNone unifs vname rhs
      | IntLiteral(nL), IntLiteral(nR) when nL = nR -> 
          Some(unifs)
      | BoolLiteral(bL), BoolLiteral(bR) when bL = bR -> 
          Some(unifs)
      | SetExpr(elistL),  SetExpr(elistR) ->
          let s1 = elistL |> __ApplyUnifs unifs 
          let s2 = elistR |> Set.ofList
          if (s1 = s2) then 
            Some(unifs)
          else
            __UnifLists elistL elistR
      | SequenceExpr(elistL), SequenceExpr(elistR) when List.length elistL = List.length elistR ->
          __UnifLists elistL elistR
      | _ when l = r ->
          Some(unifs)
      | _ ->
          let __TryUnifyPair x1 a1 x2 a2 unifs = 
            let builder = new Utils.CascadingBuilder<_>(None)
            builder {
              let! unifsLhs = SelectiveUnifyImplies okToUnifyFunc x1 a1 dir unifs
              let! unifsRhs = SelectiveUnifyImplies okToUnifyFunc x2 a2 dir unifsLhs
              return Some(unifsRhs)
            }

          // target implies candidate!
          let rec ___f2 consequence premise unifs = 
            match consequence, premise with
            // same operators + commutative -> try both
            | BinaryExpr(_, opT, lhsT, rhsT), BinaryExpr(_, opC, lhsC, rhsC) when opT = opC && IsCommutativeOp opT ->
                match __TryUnifyPair lhsC lhsT rhsC rhsT unifs with
                | Some(x) -> Some(x)
                | None -> __TryUnifyPair lhsC rhsT rhsC lhsT unifs
            // operators are the same
            | BinaryExpr(_, opT, lhsT, rhsT), BinaryExpr(_, opC, lhsC, rhsC) when opC = opT ->
                __TryUnifyPair lhsC lhsT rhsC rhsT unifs
            // operators are exactly the invers of one another
            | BinaryExpr(_, opT, lhsT, rhsT), BinaryExpr(_, opC, lhsC, rhsC) when AreInverseOps opC opT ->
                __TryUnifyPair lhsC rhsT rhsC lhsT unifs
            // 
            | BinaryExpr(_, opT, lhsT, rhsT), BinaryExpr(_, opC, lhsC, rhsC) when DoesImplyOp opC opT ->
                __TryUnifyPair lhsC lhsT rhsC rhsT unifs
            | UnaryExpr(opC, subC), UnaryExpr(opP, subP) when opC = opP ->
                SelectiveUnifyImplies okToUnifyFunc subP subC dir unifs 
            | SelectExpr(lstC, idxC), SelectExpr(lstP, idxP) ->
                __TryUnifyPair lstP lstC idxP idxC unifs
            | Dot(exprC, fldNameC), Dot(exprP, fldNameP) when fldNameC = fldNameP ->
                SelectiveUnifyImplies okToUnifyFunc exprP exprC dir unifs
            | _ -> None                     

          let rec ___f1 targetLst candidateLst unifs = 
            match targetLst, candidateLst with
            | targetExpr :: targetRest, candExpr :: candRest -> 
                // trying to find a unification for "targetExpr"
                let uOpt = match ___f2 targetExpr candExpr unifs with
                            // found -> just return
                            | Some(unifs2) -> Some(unifs2)
                            // not found -> keep looking in the rest of the candidate expressions
                            | None -> ___f1 [targetExpr] candRest unifs
                match uOpt with
                // found -> try find for the rest of the target expressions
                | Some(unifs2) -> ___f1 targetRest candidateLst unifs2
                // not found -> fail
                | None -> None
            | targetExpr :: _, [] ->
                // no more candidates for unification for this targetExpr -> fail
                None
            | [], _ ->
                // we've found unifications for all target expressions -> return the current unifications map
                Some(unifs)
                  
          let __HasSetExpr e = DescendExpr2 (fun ex acc -> if acc then true else match ex with SetExpr(_) -> true | _ -> false) e false
          let __PreprocSplitSort e = e |> DesugarAndRemove |> DistributeNegation |> SplitIntoConjunts |> List.sortBy (fun e -> if __HasSetExpr e then 1 else 0)
          let lhsConjs = lhs |> __PreprocSplitSort
          let rhsConjs = rhs |> __PreprocSplitSort
          ___f1 rhsConjs lhsConjs unifs
    with
    | CannotUnify
    | KeyAlreadyExists -> None

let UnifyImplies lhs rhs dir unifs = SelectiveUnifyImplies (fun e -> true) lhs rhs dir unifs

////////////////////////////////////////////

let rec ComputeClosure heapInst premises = 
  let bogusExpr = VarLiteral("!@#$%^&*()")

  let ApplyUnifs unifs expr =
    Rewrite (function 
               | VarLiteral(id) when unifs |> Map.containsKey id -> 
                   Some(unifs |> Map.find id)
               | _ -> None                 
            ) expr    

  let FindMatches expr except premises = 
    let okToUnifyFunc = fun (varName: string) -> varName.StartsWith("$")
    premises |> Set.toList
             |> List.choose (function BinaryExpr(_,"=",lhs,rhs) -> 
                                        if lhs = expr && not (rhs = except) then 
                                          Some(rhs)
                                        elif rhs = expr && not (lhs = except) then
                                          Some(lhs)
                                        else 
                                          match SelectiveUnifyImplies okToUnifyFunc lhs expr LTR Map.empty with
                                          | Some(unifs) -> Some(ApplyUnifs unifs rhs)
                                          | None -> 
                                              match SelectiveUnifyImplies okToUnifyFunc rhs expr LTR Map.empty with
                                              | Some(unifs) -> Some(ApplyUnifs unifs lhs)
                                              | None -> None
                                      | _ -> None)
  
  let MySetAdd expr set =
    let x = Printer.PrintExpr 0 expr
    if x.Contains("$") then
      set
    else 
      match expr with
      | BinaryExpr(p,op,lhs,rhs) when IsCommutativeOp op && Set.contains (BinaryExpr(p,op,rhs,lhs)) set -> set
      | BinaryExpr(p,op,lhs,rhs) when IsCommutativeOp op && rhs = lhs -> set
      | _ -> Set.add expr set

  let SelectExprCombinerFunc lst idx = 
    // distribute the indexing operation if possible
    let rec __fff lst idx = 
      let selExpr = SelectExpr(lst, idx)
      match lst with
      | BinaryExpr(_,"+",lhs,rhs) -> 
          let idxVal = EvalFull heapInst idx |> Expr2Int
          let lhsVal = EvalFull heapInst lhs |> Expr2List
          let rhsVal = EvalFull heapInst rhs |> Expr2List
          if idxVal < List.length lhsVal then
            __fff lhs idx
          else 
            __fff rhs (BinarySub idx (IntLiteral(List.length lhsVal)))
      | SequenceExpr(elist) -> 
          let idxVal = EvalFull heapInst idx |> Expr2Int
          [elist.[idxVal]]
      | _ -> [selExpr] 
    __fff lst idx   

  let SeqLenCombinerFunc lst =
    // distribute the SeqLength operation if possible 
    let rec __fff lst = 
      let lenExpr = SeqLength(lst)
      match lst with
      | BinaryExpr(_,"+",lhs,rhs) -> 
          BinaryAdd (__fff lhs) (__fff rhs)          
      | SequenceExpr(elist) -> 
          IntLiteral(List.length elist)
      | _ -> lenExpr
    [__fff lst]

  let rec __CombineAllMatches expr premises =
    match expr with
    | BinaryExpr(p,op,lhs,rhs) -> 
        let lhsMatches = __CombineAllMatches lhs premises
        let rhsMatches = __CombineAllMatches rhs premises
        Utils.ListCombine (fun e1 e2 -> BinaryExpr(p,op,e1,e2)) lhsMatches rhsMatches
    | UnaryExpr(op,sub) -> 
        __CombineAllMatches sub premises |> List.map (fun e -> UnaryExpr(op,e))
    | SelectExpr(lst,idx) ->
        let lst1 = FindMatches expr bogusExpr premises 
        let lstMatches = __CombineAllMatches lst premises
        let idxMatches = __CombineAllMatches idx premises
        let lst2 = Utils.ListCombineMult SelectExprCombinerFunc lstMatches idxMatches
        lst1 @ lst2
    | SeqLength(lst) -> 
        let lst1 = FindMatches expr bogusExpr premises 
        let lst2 = __CombineAllMatches lst premises |> List.map SeqLenCombinerFunc |> List.concat
        lst1 @ lst2
    // TODO: other cases
    | _ -> expr :: (FindMatches expr bogusExpr premises)

  let rec __ExpandPremise expr premises = 
    let __AddToPremisses exprLst premises = exprLst |> List.fold (fun acc e -> MySetAdd e acc) premises
    let allMatches = lazy(__CombineAllMatches expr premises)
    match expr with
    | BinaryExpr(p,op,lhs,rhs) when IsRelationalOp op ->
        let x = allMatches.Force()
        __AddToPremisses x premises
    | SelectExpr(lst, idx) ->
        let x = allMatches.Force()
        __AddToPremisses x premises
    | _ -> premises  

  let rec __Iter exprLst premises = 
    match exprLst with
    | expr :: rest ->  
        let newPremises = __ExpandPremise expr premises
        __Iter rest newPremises
    | [] -> premises

  (* --- function body starts here --- *)
  let premises' = __Iter (premises |> Set.toList) premises
  if premises' = premises then
    premises'
  else 
    ComputeClosure heapInst premises' 


