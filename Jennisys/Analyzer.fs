﻿module Analyzer

open Ast
open AstUtils
open CodeGen
open DafnyModelUtils
open MethodUnifier
open Modularizer
open PipelineUtils
open Options
open Printer
open Resolver
open DafnyPrinter
open Utils

open Microsoft.Boogie

let Rename suffix vars =
  vars |> List.map (function Var(nm,tp) -> nm, Var(nm + suffix, tp))

let ReplaceName substMap nm =
  match Map.tryFind nm substMap with
    | Some(Var(name, tp)) -> name
    | None -> nm

let rec Substitute substMap = function
  | IdLiteral(s) -> IdLiteral(ReplaceName substMap s)
  | Dot(e,f) -> Dot(Substitute substMap e, ReplaceName substMap f)
  | UnaryExpr(op,e) -> UnaryExpr(op, Substitute substMap e)
  | BinaryExpr(n,op,e0,e1) -> BinaryExpr(n, op, Substitute substMap e0, Substitute substMap e1)
  | SelectExpr(e0,e1) -> SelectExpr(Substitute substMap e0, Substitute substMap e1)
  | UpdateExpr(e0,e1,e2) -> UpdateExpr(Substitute substMap e0, Substitute substMap e1, Substitute substMap e2)
  | SequenceExpr(ee) -> SequenceExpr(List.map (Substitute substMap) ee)
  | SeqLength(e) -> SeqLength(Substitute substMap e)
  | ForallExpr(vv,e) -> ForallExpr(vv, Substitute substMap e)
  | expr -> expr

let GenMethodAnalysisCode comp m assertion =
  let methodName = GetMethodName m
  let signature = GetMethodSig m 
  let ppre,ppost = GetMethodPrePost m 
  let pre = Desugar ppre
  let post = Desugar ppost
  "  method " + methodName + "()" + newline +
  "    modifies this;" + newline +
  "  {" + newline + 
  // print signature as local variables
  (match signature with
    | Sig(ins,outs) ->
        List.concat [ins; outs] |> List.fold (fun acc vd -> acc + (sprintf "    var %s;" (PrintVarDecl vd)) + newline) "") +
  "    // assume precondition" + newline +
  "    assume " + (PrintExpr 0 pre) + ";" + newline + 
  "    // assume invariant and postcondition" + newline + 
  "    assume Valid();" + newline +
  "    assume " + (PrintExpr 0 post) + ";" + newline +
  "    // assume user defined invariant again because assuming Valid() doesn't always work" + newline +
  (GetInvariantsAsList comp |> PrintSep newline (fun e -> "    assume " + (PrintExpr 0 e) + ";")) + newline +
  // if the following assert fails, the model hints at what code to generate; if the verification succeeds, an implementation would be infeasible
  "    // assert false to search for a model satisfying the assumed constraints" + newline + 
  "    assert " + (PrintExpr 0 assertion) + ";" + newline + 
  "  }" + newline
             
let rec MethodAnalysisPrinter onlyForThese assertion comp = 
  let cname = GetComponentName comp
  match onlyForThese with
  | (c,m) :: rest when GetComponentName c = cname -> 
    match m with 
    | Method(methodName, sign, pre, post, true) -> 
        (GenMethodAnalysisCode c m assertion) + newline +
        (MethodAnalysisPrinter rest assertion comp)
    | _ -> ""
  | _ :: rest -> MethodAnalysisPrinter rest assertion comp
  | [] -> ""

let rec IsArgsOnly args expr = 
  match expr with                                                
  | IntLiteral(_)                        -> true
  | BoolLiteral(_)                       -> true
  | Star                                 -> true
  | ObjLiteral(id)                       -> true
  | VarLiteral(id)
  | IdLiteral(id)                        -> args |> List.exists (function Var(varName,_) when varName = id -> true | _ -> false)
  | UnaryExpr(_,e)                       -> IsArgsOnly args e
  | BinaryExpr(_,_,e1,e2)                -> (IsArgsOnly args e1) && (IsArgsOnly args e2)
  | IteExpr(c,e1,e2)                     -> (IsArgsOnly args c) && (IsArgsOnly args e1) && (IsArgsOnly args e2)
  | Dot(e,_)                             -> IsArgsOnly args e
  | SelectExpr(e1, e2)                   -> (IsArgsOnly args e1) && (IsArgsOnly args e2)
  | UpdateExpr(e1, e2, e3)               -> (IsArgsOnly args e1) && (IsArgsOnly args e2) && (IsArgsOnly args e3)
  | SequenceExpr(exprs) | SetExpr(exprs) -> exprs |> List.fold (fun acc e -> acc && (IsArgsOnly args e)) true
  | SeqLength(e)                         -> IsArgsOnly args e
  | ForallExpr(vars,e)                   -> IsArgsOnly (List.concat [args; vars]) e
  | MethodCall(rcv,_,aparams)            -> rcv :: aparams |> List.fold (fun acc e -> acc && (IsArgsOnly args e)) true

let AddUnif indent e v unifMap =
  let idt = Indent indent
  let builder = new CascadingBuilder<_>(unifMap)
  builder {
    let! notAlreadyAdded = Map.tryFind e unifMap |> Utils.IsNoneOption |> Utils.BoolToOption
    Logger.DebugLine (idt + "    - adding unification " + (PrintExpr 0 e) + " <--> " + (PrintConst v))
    return Map.add e v unifMap
  }

//TODO: unifications should probably by "Expr <--> Expr" instead of "Expr <--> Const"
let rec GetUnifications indent args heapInst unifs expr =
  let idt = Indent indent
  // - first looks if the give expression talks only about method arguments (args)
  // - then checks if it doesn't already exist in the unification map
  // - then it tries to evaluate it to a constant
  // - if all of these succeed, it adds a unification rule e <--> val(e) to the given unifMap map
  let __AddUnif e unifsAcc =
    let builder = new CascadingBuilder<_>(unifsAcc)
    builder {
      let! argsOnly = IsArgsOnly args e |> Utils.BoolToOption
      let! v = try Some(Eval heapInst (fun _ -> true) e |> Expr2Const) with ex -> None
      return AddUnif indent e v unifsAcc
    }
  (* --- function body starts here --- *)
  AstUtils.DescendExpr2 __AddUnif expr unifs

//  =======================================================
/// Returns a map (Expr |--> Const) containing unifications
/// found for the given method and heap/env/ctx
//  =======================================================
let GetUnificationsForMethod indent comp m heapInst =
  let idt = Indent indent
  let rec GetArgValueUnifications args = 
    match args with
    | Var(name,_) :: rest -> 
        match Map.tryFind name heapInst.methodArgs with
        | Some(c) ->
            GetArgValueUnifications rest |> AddUnif indent (VarLiteral(name)) c
        | None -> failwith ("couldn't find value for argument " + name)
    | [] -> Map.empty
  (* --- function body starts here --- *)
  match m with
  | Method(mName,Sig(ins, outs),pre,post,_) -> 
      let args = List.concat [ins; outs]
      match args with 
      | [] -> Map.empty
      | _  -> let unifs = GetArgValueUnifications args
              GetUnifications indent args heapInst unifs (BinaryAnd pre post)
              
  | _ -> failwith ("not a method: " + m.ToString())

//  =========================================================================
/// For a given constant "o" (which is an object, something like "gensym32"), 
/// finds a path of field references from "this". 
///
/// Implements a backtracking search over the heap entries to find that
/// path.  It starts from the given object, and follows the backpointers
/// until it reaches the root ("this")
//  ========================================================================= 
let objRef2ExprCache = new System.Collections.Generic.Dictionary<string, Expr>()
let GetObjRefExpr objRefName (heapInst: HeapInstance) = 
  let rec __GetObjRefExpr objRefName visited = 
    if Set.contains objRefName visited then 
      None
    else 
      let newVisited = Set.add objRefName visited
      match objRefName with
      | "this" -> Some(ObjLiteral("this"))
      | _ -> 
          let rec __fff lst = 
            match lst with
            | ((o,Var(fldName,_)),_) :: rest -> 
                match __GetObjRefExpr o.name newVisited with
                | Some(expr) -> Some(Dot(expr, fldName))
                | None -> __fff rest
            | [] -> None
          let backPointers = heapInst.assignments |> List.filter (fun ((_,_),l) -> l = ObjLiteral(objRefName))
          __fff backPointers 
  (* --- function body starts here --- *)
  if objRef2ExprCache.ContainsKey(objRefName) then
    Some(objRef2ExprCache.[objRefName])
  else
    let res = __GetObjRefExpr objRefName (Set.empty)
    match res with 
    | Some(e) -> objRef2ExprCache.Add(objRefName, e)
    | None -> ()
    res

//  =======================================================
/// Applies given unifications onto the given heap/env/ctx
/// 
/// If "conservative" is true, applies only those that 
/// can be verified to hold, otherwise applies all of them
//  =======================================================
let rec ApplyUnifications indent prog comp mthd unifs heapInst conservative = 
  let idt = Indent indent
  let __CheckUnif o f e idx =
    if not conservative || not Options.CONFIG.checkUnifications then 
      true 
    else
      let objRefExpr = GetObjRefExpr o heapInst |> Utils.ExtractOptionMsg ("Couldn't find a path from 'this' to " + o)
      let fldName = PrintVarName f                             
      let lhs = Dot(objRefExpr, fldName)
      let assertionExpr = match f with
                          | Var(_, Some(SeqType(_))) when not (idx = -1) -> BinaryEq (SelectExpr(lhs, IntLiteral(idx))) e
                          | Var(_, Some(SetType(_))) when not (idx = -1) -> BinaryIn e lhs
                          | _                                            -> BinaryEq lhs e 
      // check if the assertion follows and if so update the env
      let code = PrintDafnyCodeSkeleton prog (MethodAnalysisPrinter [comp,mthd] assertionExpr) false
      Logger.Debug (idt + "    - checking assertion: " + (PrintExpr 0 assertionExpr) + " ... ")
      let ok = CheckDafnyProgram code ("unif_" + (GetMethodFullName comp mthd))
      if ok then
        Logger.DebugLine " HOLDS"
      else
        Logger.DebugLine " DOESN'T HOLD"
      ok
  (* --- function body starts here --- *)
  match unifs with
  | (e,c) :: rest -> 
      let heapInst = ApplyUnifications indent prog comp mthd rest heapInst conservative
      let newHeap = heapInst.assignments|> List.fold (fun acc ((o,f),value) ->
                                                       if value = Const2Expr c then
                                                         if __CheckUnif o.name f e -1 then                                                
                                                           // change the value to expression
                                                           //Logger.TraceLine (sprintf "%s    - applied: %s.%s --> %s" idt (PrintConst o) (GetVarName f) (PrintExpr 0 e) )
                                                           Utils.ListMapAdd (o,f) e acc 
                                                         else
                                                           // don't change the value unless "conservative = false"
                                                           Utils.ListMapAdd (o,f) value acc
                                                       else 
                                                         let rec __UnifyOverLst lst cnt =
                                                               match lst with
                                                               | lstElem :: rest when lstElem = Const2Expr c ->
                                                                   if __CheckUnif o.name f e cnt then
                                                                     //Logger.TraceLine (sprintf "%s    - applied: %s.%s[%d] --> %s" idt (PrintConst o) (GetVarName f) cnt (PrintExpr 0 e) )
                                                                     e :: __UnifyOverLst rest (cnt+1)
                                                                   else  
                                                                     lstElem :: __UnifyOverLst rest (cnt+1)
                                                               | lstElem :: rest ->
                                                                   lstElem :: __UnifyOverLst rest (cnt+1)
                                                               | [] -> []
                                                         // see if it's a list, then try to match its elements, otherwise leave it as is
                                                         match value with
                                                         | SequenceExpr(elist) -> 
                                                             let newExprList = __UnifyOverLst elist 0
                                                             Utils.ListMapAdd (o,f) (SequenceExpr(newExprList)) acc
                                                         | SetExpr(elist) ->
                                                             let newExprList = __UnifyOverLst elist 0
                                                             Utils.ListMapAdd (o,f) (SetExpr(newExprList)) acc
                                                         | _ -> 
                                                             Utils.ListMapAdd (o,f) value acc
                                                     ) heapInst.assignments
      {heapInst with assignments = newHeap }
  | [] -> heapInst

//  ====================================================================================
/// Returns whether the code synthesized for the given method can be verified with Dafny
//  ====================================================================================
let VerifySolution prog solutions genRepr =
  // print the solution to file and try to verify it with Dafny
  //let prog = Program(solutions |> Utils.MapKeys |> Map.ofList |> Utils.MapKeys)
  let code = PrintImplCode prog solutions genRepr
  CheckDafnyProgram code dafnyVerifySuffix

let rec DiscoverAliasing exprList heapInst = 
  match exprList with
  | e1 :: rest -> 
      let eqExpr = rest |> List.fold (fun acc e -> 
                                        if Eval heapInst (fun _ -> true) (BinaryEq e1 e) = TrueLiteral then
                                          BinaryAnd acc (BinaryEq e1 e)
                                        else
                                          acc
                                     ) TrueLiteral
      BinaryAnd eqExpr (DiscoverAliasing rest heapInst)
  | [] -> TrueLiteral

//  =============================================================================
/// Returns an expression that combines the post-condition of a given method with
/// invariants for all objects present on the heap
//  =============================================================================
let GetHeapExpr prog mthd heapInst = 
  // get expressions to evaluate:
  //   - add post (and pre?) conditions                                     
  //   - go through all objects on the heap and assert their invariants  
  let pre,post = GetMethodPrePost mthd
  let prepostExpr = post //TODO: do we need the "pre" here as well?
  let heapObjs = heapInst.assignments |> List.fold (fun acc ((o,_),_) -> acc |> Set.add o) Set.empty
  heapObjs |> Set.fold (fun acc o -> 
                          let receiverOpt = GetObjRefExpr o.name heapInst
                          let receiver = Utils.ExtractOption receiverOpt
                          let objComp = FindComponent prog (GetTypeShortName o.objType) |> Utils.ExtractOption
                          let objInvs = GetInvariantsAsList objComp
                          let objInvsUpdated = objInvs |> List.map (ChangeThisReceiver receiver)
                          objInvsUpdated |> List.fold (fun a e -> BinaryAnd a e) acc
                      ) prepostExpr

let FindExisting indent comp m cond =
  if not Options.CONFIG.genMod then
    None
  else 
    let idt = Indent indent
    match TryFindAMatch m (GetMembers comp |> FilterMethodMembers) with
    | Some(m',unifs) -> 
        Logger.InfoLine (idt + "    - substitution method found:")
        Logger.InfoLine (PrintMethodSignFull (indent+6) comp m')
        let args = ApplyMethodUnifs m' unifs
        let delegateCall = MethodCall(ThisLiteral, GetMethodName m', args)
        let obj = { name = "this"; objType = GetClassType comp }
        let var = Var("", None)
        let body = [(obj,var), delegateCall]
        let hInst = { assignments = body; 
                      methodArgs  = Map.empty; 
                      globals     = Map.empty }
        Some(Map.empty |> Map.add (comp,m) [cond, hInst]
                       |> Map.add (comp,m') [])
    | None -> None

//  ============================================================================
/// Attempts to synthesize the initialization code for the given constructor "m"
///
/// Returns a (heap,env,ctx) tuple
//  ============================================================================                           
let rec AnalyzeConstructor indent prog comp m =
  let idt = Indent indent
  Logger.InfoLine (idt + "[*] Analyzing constructor")
  Logger.InfoLine (idt + "------------------------------------------")
  Logger.InfoLine (PrintMethodSignFull (indent + 4) comp m)
  Logger.InfoLine (idt + "------------------------------------------")
  let methodName = GetMethodName m
  let pre,post = GetMethodPrePost m
  // generate Dafny code for analysis first
  let code = PrintDafnyCodeSkeleton prog (MethodAnalysisPrinter [comp,m] FalseLiteral) false
  Logger.Info     (idt + "    - searching for an instance      ...")
  let models = RunDafnyProgram code (dafnyScratchSuffix + "_" + (GetMethodFullName comp m))  
  if models.Count = 0 then
    // no models means that the "assert false" was verified, which means that the spec is inconsistent
    Logger.WarnLine (idt + " !!! SPEC IS INCONSISTENT !!!")
    Map.empty
  else 
    if models.Count > 1 then 
      Logger.WarnLine " FAILED "
      failwith "internal error (more than one model for a single constructor analysis)"
    Logger.InfoLine " OK "
    let model = models.[0]
    let hModel = ReadFieldValuesFromModel model prog comp m
    let heapInst = ResolveModel hModel
    let unifs = GetUnificationsForMethod indent comp m heapInst |> Map.toList
    let heapInst = ApplyUnifications indent prog comp m unifs heapInst true
        
    // split into method calls
    let sol = MakeModular indent prog comp m TrueLiteral heapInst

    if Options.CONFIG.verifySolutions then
      Logger.InfoLine (idt + "    - verifying synthesized solution ... ")
      let verified = VerifySolution prog sol Options.CONFIG.genRepr
      Logger.Info (idt + "    ")
      if verified then
        Logger.InfoLine "~~~ VERIFIED ~~~"
        sol
      else 
        Logger.InfoLine "!!! NOT VERIFIED !!!"
        if Options.CONFIG.inferConditionals then
          Logger.InfoLine (idt + "    Strengthening the pre-condition")
          TryInferConditionals (indent + 4) prog comp m unifs heapInst
        else
          sol      
    else
      sol
and TryInferConditionals indent prog comp m unifs heapInst = 
  // use the orginal method, not the one with an extra precondition
  let __FixSolution sol =
    sol |> Map.fold (fun acc (cc,mm) v -> 
                       if GetMethodName mm = GetMethodName m then
                         acc |> Map.add (cc,m) v
                       else 
                         acc |> Map.add (cc,mm) v) Map.empty
  let idt = Indent indent
  let wrongSol = Utils.MapSingleton (comp,m) [TrueLiteral, heapInst]
  let heapInst2 = ApplyUnifications indent prog comp m unifs heapInst false
  let expr = GetHeapExpr prog m heapInst2
  // now evaluate and see what's left
  let newCond = Eval heapInst2 (function VarLiteral(_) -> false | _ -> true) expr
  if newCond = TrueLiteral then
    Logger.InfoLine (sprintf "%s    - no more interesting pre-conditions" idt)
    wrongSol
  else
    let candCond = 
      if newCond = FalseLiteral then
        // it must be because there is some aliasing going on between method arguments, 
        // so we should try that as a candidate pre-condition
        let tmp = DiscoverAliasing (GetMethodArgs m |> List.map (function Var(name,_) -> VarLiteral(name))) heapInst2
        if tmp = TrueLiteral then failwith ("post-condition evaluated to false and no aliasing was discovered")
        tmp
      else 
        newCond
    Logger.InfoLine (sprintf "%s    - candidate pre-condition: %s" idt (PrintExpr 0 candCond))
    let _,_,m2 = AddPrecondition prog comp m candCond
    let sol = MakeModular indent prog comp m2 candCond heapInst2
    Logger.Info (idt + "    - verifying partial solution ... ")
    let verified = 
      if Options.CONFIG.verifyPartialSolutions then
        VerifySolution prog sol Options.CONFIG.genRepr
      else 
        true
    if verified then
      if Options.CONFIG.verifyPartialSolutions then Logger.InfoLine "VERIFIED" else Logger.InfoLine "SKIPPED"
      let solThis = match FindExisting indent comp m2 candCond with
                    | Some(sol2) -> sol2
                    | None -> sol   
      let _,_,m3 = AddPrecondition prog comp m (UnaryNot(candCond))
      let solRest = match FindExisting indent comp m3 (UnaryNot(candCond)) with
                    | Some(sol3) -> sol3
                    | None -> AnalyzeConstructor (indent + 2) prog comp m3
      MergeSolutions (__FixSolution solThis) (__FixSolution solRest)
    else 
      Logger.InfoLine "NOT VERIFIED"
      wrongSol  
      
let GetMethodsToAnalyze prog =
  let mOpt = Options.CONFIG.methodToSynth;
  if mOpt = "*" then
    (* all *)
    FilterMembers prog FilterConstructorMembers
  elif mOpt = "paramsOnly" then
    (* only with parameters *)
    FilterMembers prog FilterConstructorMembersWithParams 
  else
    let allMethods,neg = 
      if mOpt.StartsWith("~") then
        mOpt.Substring(1), true
      else
        mOpt, false
    (* exactly one *)
    let methods = allMethods.Split([|','|])
    let lst = methods |> Array.fold (fun acc m -> 
                                       let idx = m.LastIndexOf(".")
                                       if idx = -1 || idx = m.Length - 1 then
                                         raise (InvalidCmdLineArg("Invalid method full name: " + m))
                                       let compName = m.Substring(0, idx)
                                       let methName = m.Substring(idx + 1)
                                       let c = FindComponent prog compName |> Utils.ExtractOptionMsg ("Cannot find component " + compName)
                                       let mthd = FindMethod c methName |> Utils.ExtractOptionMsg ("Cannot find method " + methName + " in component " + compName)
                                       (c,mthd) :: acc
                                    ) []
    if neg then
      FilterMembers prog FilterConstructorMembers |> List.filter (fun e -> not (Utils.ListContains e lst))
    else
      lst
  

// ============================================================================
/// Goes through a given list of methods of the given program and attempts to 
/// synthesize code for each one of them.
///
/// Returns a map from (component * method) |--> Expr * HeapInstance
// ============================================================================
let rec AnalyzeMethods prog members solutionsSoFar = 
  let __IsAlreadySolved c m solutionMap = 
      let existingKey = solutionMap |> Map.tryFindKey (fun (cc,mm) v -> CheckSameMethods (c,m) (cc,mm) && not (v = [])) 
      match existingKey with
      | Some(_) -> true
      | None -> false

  let rec __AnalyzeConstructorDeep prog mList solutionsSoFar = 
    match mList with
    | (comp,mthd) :: rest -> 
        if not (__IsAlreadySolved comp mthd solutionsSoFar) then
          let sol = AnalyzeConstructor 2 prog comp mthd
          let unsolved = sol |> Map.filter (fun (c,m) lst -> lst = [] && not(__IsAlreadySolved c m solutionsSoFar)) |> Utils.MapKeys
          let newSols = solutionsSoFar |> MergeSolutions sol
          __AnalyzeConstructorDeep prog (rest@unsolved) newSols
        else
          __AnalyzeConstructorDeep prog rest solutionsSoFar
    | [] -> solutionsSoFar
  
  (* --- function body starts here --- *)
  match members with
  | (comp,m) :: rest -> 
      match m with
      | Method(_,_,_,_,true) -> 
          let sol = __AnalyzeConstructorDeep prog [comp,m] solutionsSoFar
          Logger.InfoLine ""
          AnalyzeMethods prog rest sol
      | _ -> AnalyzeMethods prog rest solutionsSoFar
  | [] -> solutionsSoFar

let Analyze prog filename =
  let rec __AddMethodsFromProg methods solutions = 
    match methods with
    | (c,m) :: rest -> 
        let exists = solutions |> Map.tryFindKey (fun (c1,m1) _ -> CheckSameMethods (c,m) (c1,m1))
        match exists with
        | Some(_) -> __AddMethodsFromProg rest solutions
        | None -> __AddMethodsFromProg rest (solutions |> Map.add (c,m) [])
    | [] -> solutions

  /// Prints given solutions to a file
  let __PrintSolution prog outFileName solutions = 
    use file = System.IO.File.CreateText(outFileName)
    file.AutoFlush <- true  
    //let prog = Program(solutions |> Utils.MapKeys |> Map.ofList |> Utils.MapKeys)
    // add all other methods (those for which we don't have synthesized solution) as well
    let allMethods = FilterMembers prog FilterConstructorMembers
    let extSolutions = solutions //__AddMethodsFromProg allMethods solutions
    let synthCode = PrintImplCode prog extSolutions Options.CONFIG.genRepr
    fprintfn file "%s" synthCode

  (* --- function body starts here --- *)
  let solutions = AnalyzeMethods prog (GetMethodsToAnalyze prog) Map.empty
  let progName = System.IO.Path.GetFileNameWithoutExtension(filename)
  let outFlatSolFileName = dafnySynthFileNameTemplate.Replace("###", progName)
  Logger.InfoLine "Printing synthesized code"
  __PrintSolution prog outFlatSolFileName solutions
  ()

//let AnalyzeComponent_rustan c =
//  match c with
//  | Component(Class(name,typeParams,members), Model(_,_,cVars,frame,inv), code) ->
//      let aVars = Fields members
//      let aVars0 = Rename "0" aVars
//      let aVars1 = Rename "1" aVars
//      let allVars = List.concat [aVars; List.map (fun (a,b) -> b) aVars0; List.map (fun (a,b) -> b) aVars1; cVars]
//      let inv0 = Substitute (Map.ofList aVars0) inv
//      let inv1 = Substitute (Map.ofList aVars1) inv
//      // Now print it as a Dafny program
//      printf "class %s" name
//      match typeParams with
//        | [] -> ()
//        | _  -> printf "<%s>"  (typeParams |> PrintSep ", " (fun tp -> tp))
//      printfn " {"
//      // the fields: original abstract fields plus two more copies thereof, plus and concrete fields
//      allVars |> List.iter (function Var(nm,None) -> printfn "  var %s;" nm | Var(nm,Some(tp)) -> printfn "  var %s: %s;" nm (PrintType tp))
//      // the method
//      printfn "  method %s_checkInjective() {" name
//      printf "    assume " ; (VarsAreDifferent aVars0 aVars1) ; printfn ";"
//      printfn "    assume %s;" (PrintExpr 0 inv0)
//      printfn "    assume %s;" (PrintExpr 0 inv1)
//      printfn "    assert false;" // {:msg "Two abstract states map to the same concrete state"}
//      printfn "  }"
//      // generate code
//      members |> List.iter (function
//        | Constructor(methodName,signature,pre,stmts) -> printf "%s" (GenerateCode methodName signature pre stmts inv false)
//        | Method(methodName,signature,pre,stmts) -> printf "%s" (GenerateCode methodName signature pre stmts inv true)
//        | _ -> ())
//      // the end of the class
//      printfn "}"
//  | _ -> assert false  // unexpected case