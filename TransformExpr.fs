#if INTERACTIVE
#r "Mono.Reflection.dll"
#endif

open System
open System.Reflection
open System.Runtime.CompilerServices
open Microsoft.FSharp.Quotations
open Patterns
open DerivedPatterns

let inline replace name value (map: Map<_, _>) = Map.remove name map |> Map.add name value

// your implementation could be here =)
let mduration settlement maturity coupon yld frequency basis = 0M
let accrint issue first_interest settlement rate par frequency basis = 0M


open Mono.Reflection

/// Get reflected definition from a closure obj with Mono.Reflection
let defForObj() =
    let cache = System.Collections.Generic.Dictionary<_, _>()
    fun obj ->
        let ty = obj.GetType()
        if cache.ContainsKey ty then cache.[ty]
        else
            let methods = ty.GetMethods()

            // find closure's invoke
            let invoke = 
                methods |> Array.tryFind (fun mi -> mi.DeclaringType = ty && mi.Name = "Invoke")
            let def = 
                match invoke with
                | Some mi -> 
                    // invoke methods does have a body, so we don't check it for null 
                    // (but e.g. GetType doesn't)
                    mi.GetInstructions() 
                    |> Seq.tryFind (fun instr -> instr.OpCode = Emit.OpCodes.Call)
                    |> Option.bind (fun instr -> 
                        instr.Operand :?> System.Reflection.MethodInfo // original function info
                        |> Expr.TryGetReflectedDefinition)
                | _ -> None
            cache.Add(ty, def)
            def

/// Get reflected definition for method info 
let defForMethodInfo() =
    let cache = System.Collections.Generic.Dictionary<_,_>()
    fun mi -> 
        if cache.ContainsKey mi then cache.[mi]
        else
            let res = Expr.TryGetReflectedDefinition mi
            cache.Add (mi, res)
            res

/// Simplified SpecificCall
let inline (|Func|_|) expr =
    match expr with
    | Lambdas(_,(Call(_,minfo1,_))) -> function
        | Call(obj, minfo2, args) when minfo1.MetadataToken = minfo2.MetadataToken ->
            Some args
        | _ -> None
    | _ -> failwith "invalid template parameter"

/// Info for the transformation steps
type Info = {
    Scope: Map<string, Expr>
    Prior: int
    RightOperand: bool
}

/// Generate a formula pattern for an expression
/// column number - index of var in the env; '#' is a temp placeholder for a row number
let generatePattern expr env =     
    // Defaults
    let scope = 
        Seq.mapi (fun i name -> name, Expr.Value("R#C" + string (i + 1))) env 
        |> Map.ofSeq

    let defaultPrior = 4
    let tryGetReflectedDef = defForMethodInfo()

    // info with default priority
    let inline (!!) info = { info with Prior = defaultPrior }(*[/omit]*)
    
    // check if we need to add the parens
    let inline addParens (info: Info) currPrior = 
        info.Prior < currPrior || (info.Prior = currPrior && info.RightOperand)

    // let x = ...; let f x y = ...; f 1 x
    // inside f x is Value(1), y is outer x value 
    let updateScope names parms scope =
        Seq.zip names parms
        |> Seq.fold (fun sc -> function
            | name, Var var -> replace name sc.[var.Name] sc
            | name, param -> replace name param sc) scope

    // print binary ops: (x op y)
    let rec inline printBinaryOp op x y curr (info: Info) = 
        let left = transform { info with Prior = curr; RightOperand = false } x
        let right = transform { info with Prior = curr; RightOperand = true } y
        let res = left + op + right in if addParens info curr then "(" + res +  ")" else res

    // print functions: name(arg1, arg2, ...)
    and inline printFunc name args info  =
        let argValues: string[] = List.map (transform !!info) args |> List.toArray
        sprintf "%s(%s)" name (String.Join (", ", argValues))

    // apply function with given parameters
    and applyFunc (var: Var, parms) info =
        match Map.tryFind var.Name info.Scope with
        | Some (Lambdas (((x::_)::_) as vars, expr)) ->
            let newScope = 
                let names = seq { for [v] in vars do yield v.Name }
                updateScope names parms info.Scope

            transform { info with Scope = newScope } expr
        | _ -> failwith "cannot apply function"

    // transform an expression into pattern
    and transform (info: Info) = function
        | Func <@@ (+) @@> [x; y] -> printBinaryOp "+" x y 3 info
        | Func <@@ (-) @@> [x; y] -> printBinaryOp "-" x y 3 info
        | Func <@@ (*) @@> [x; y] -> printBinaryOp "*" x y 2 info
        | Func <@@ (/) @@> [x; y] -> printBinaryOp "/" x y 2 info
        | Func <@@ ( ** ) @@> [x; y] -> printBinaryOp "^" x y 1 info 
        | Func <@@ (~-) @@> [x] -> "-" + transform { info with Prior = 0 } x
        | Func <@@ (~+) @@> [x] -> transform { info with Prior = 0 } x
        | Func <@@ decimal @@> [x] 
        | Func <@@ double @@> [x] 
        | Func <@@ float @@> [x] -> string (transform { info with Prior = 0 } x)

        | Func <@@ mduration @@> args -> printFunc "MDURATION" args info
        | Func <@@ accrint @@> args -> printFunc "ACCRINT" args info
        | Lambdas (_, e) -> transform !!info e

        // let a, b, ... = 1, 2, ...
        // Note: nested tuples and tuples as return values are not supported
        | Let (_, NewTuple vs, e) -> 
            let res, newScope = // Update the scope]
                let tupleItems = List.toArray vs
                let rec initTuple (e, newScope) =   
                    match e with
                    | Let(v, TupleGet(_, ind), e') -> 
                        initTuple (e', replace v.Name tupleItems.[ind] newScope)
                    | _ -> e, newScope
                initTuple (e, info.Scope)
            transform { info with Scope = newScope } res

        | Let (var, value, e) -> 
            transform { info with Scope = replace var.Name value info.Scope } e

        | Value (v, _) -> string v
        // try to replace a varname with its column index
        | Var var -> 
            match Map.tryFind var.Name info.Scope with
            | Some replacement -> transform info replacement
            | _ -> var.Name
        // args.[i] means reference to the (i+1)th column
        | Call(None, mi, _::[Value (i, _)]) when mi.DeclaringType.Name = "IntrinsicFunctions" 
                                              && mi.Name = "GetArray" -> 
            let ind = unbox i in "R#C" + string (ind + 1)
        
        // replace MakeDecimal with a value
        | Call(None, mi, Value (v, _)::_) when mi.DeclaringType.Name = "IntrinsicFunctions" 
                                            && mi.Name = "MakeDecimal"  -> 
            string v
        // try to inline a method call
        | Call(None, mi, ps) ->
            let names = mi.GetParameters() |> Array.map (fun p -> p.Name)
            let newScope = updateScope names ps info.Scope
            
            match tryGetReflectedDef mi with
            | Some impl ->
                let rec getCall e =
                    match e with
                    | Lambda(_, Lambda (_, e)) -> getCall e // skip parameters
                    | call -> call
                transform { info with Scope = newScope } (getCall impl)
                
            | _ -> failwith (sprintf "Can't get reflected definition for %s" mi.Name)
        // DateTime ctor -> Excel DATE function: DATE(year, month, day)
        | NewObject(ci, Value(y,_)::Value(m,_)::Value(d,_)::_) 
                                                when ci.DeclaringType.Name="DateTime"-> 
            sprintf "DATE(%A, %A, %A)" y m d    

        | Application ((Application args) as f, value) -> 
            // collect params for the chain of function applications
            let rec collectParams parms = function
                | Application(Var f, v) -> f, v :: parms
                | Application(applArgs, v) -> collectParams (v :: parms) applArgs
                | expr -> failwith (sprintf "unexpected expression collecting params: %A" expr)
            applyFunc (collectParams [value] f) info

        | Application (Var f, value) ->  applyFunc (f, [value]) info

        | LetRecursive _ -> failwith "Recursive functions are not supported"
        | expr -> failwith (sprintf "Unknown expression type: %A" expr)

    "=" + transform { Prior = defaultPrior; Scope = scope; RightOperand = false } expr

module PatternsExample =
    [<ReflectedDefinition>]
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let id x = x

    [<ReflectedDefinition>]
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let sum (a: decimal) b = id a + b
    
    let patterns = [
        <@@ sum 42M (decimal 42) @@>, [], "=42+42"
        
        <@@ 
            let f a b = b ** a 
            let f2() = f 3. 2. + double 0
            f2() 
         @@>, [], "=2^3+0"

        <@@ ((1. + 2.**3.) * 3. - 4.) / 5. @@>, [], "=((1+2^3)*3-4)/5"
        <@@ fun (x: decimal) -> -x + +1M @@>, [], "=-x+1"
        <@@ fun a b -> a ** 4. @@>, ["a"; "b"], "=R#C1^4"
        
        // arrays can be used instead of explicit var names
        <@@ fun (args: _ array) -> args.[0] + args.[1] @@>, [], "=R#C1+R#C2"

        <@@ fun issue settlement -> 
                accrint issue (DateTime(2010,9,8)) settlement 10 100 2 0 
         @@>, [ "issue"; "settlement"], "=ACCRINT(R#C1, DATE(2010, 9, 8), R#C2, 10, 100, 2, 0)"

        <@@ fun x ->
                let a, b = 4., 0.0001
                a * 43. - (let x = 1. in 1. - (let x = 2.-3./6. in x) + x) / b + x
         @@>, ["x"], "=4*43-(1-(2-3/6)+1)/0.0001+R#C1"

        <@@ (1.+2.)-(4.+5.) @@>, [], "=1+2-(4+5)"
    ]
 
    let run() = 
        patterns
        |> List.iteri (fun i (expr, env, expected) -> 
                let res = generatePattern expr env
                if res <> expected then printfn "test %d: %s; expected %s" i res expected)

PatternsExample.run()

module Test =
    [<ReflectedDefinition>]
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let sum (a: decimal) b = a + b
  
    [<ReflectedDefinition>]
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let mdurationMonth m c y f basis = 
        (mduration (DateTime(2012, 1, 7)) m c y f basis) * 12M

    let run export = 
        let getReflectedDefinition = defForObj()

        let data: obj [][] = [|
            [| 42M; 1M; DateTime(2012, 1, 7); DateTime(2030, 1, 1); 15M; 0.9M; 1; 1 |]
            [| null; null; null; DateTime(2016, 1, 7); 8M; 9M; 2; 1 |]
        |]

        // the vars with such names will be replaced with R{rownum}C{var index + 1}
        let dataColumns = ["a"; "b"; "s"; "m"; "c"; "y"; "f"; "basis"]
        let funcs: obj list = [sum; mdurationMonth]

        // transform quotation into a pattern & split by the rownum replacement '#'
        let unquote = 
            Option.bind (fun expr -> 
                try 
                    Some ((generatePattern expr dataColumns).Split [|'#'|])
                with _ -> None)

        // reflected definitions -> formulae
        let formulae = funcs
                        |> List.map (getReflectedDefinition >> unquote)
                        |> List.filter Option.isSome
                        |> List.map Option.get
        
        data |> Array.iteri (export formulae)

/// Export data with given pattern
let export exportValue exportFunc (patterns: string[] list) row (items: _[]) =
    let row = row + 1
    let j = items.Length + 1
    let formulae = patterns |> List.map (fun arr -> String.Join (string row, arr))
    Array.iteri (exportValue (row, 1)) items
    List.iteri (exportFunc (row, j)) formulae

// Standard output
Test.run (export 
            (fun (row, fst) col item -> printfn "Cells.[%d, %d]<-%A" row (col+fst) item)
            (fun (row, fst) col formula -> 
                printfn "Cells.[%d, %d].Formula <- \"%s\"" row (col+fst) formula))

#if INTERACTIVE
#r "Microsoft.Office.Interop.Excel"
#endif

open Microsoft.Office.Interop.Excel

let app = new ApplicationClass()
let workbook = app.Workbooks.Add(XlWBATemplate.xlWBATWorksheet)
let worksheet = workbook.Worksheets.[1] :?> Worksheet

// fill the cells
Test.run (export 
            (fun (row,fst) col item -> worksheet.Cells.[row, col+fst] <- item)
            (fun (row,fst) col formula -> 
                (worksheet.Cells.[row, col+fst] :?> Range).Formula <- formula))

app.ReferenceStyle <- XlReferenceStyle.xlA1

// Close workbook and release objects
let inline release (objs: obj list) = 
    List.iter (System.Runtime.InteropServices.Marshal.ReleaseComObject >> ignore) objs

let filename = "test.xls"
try workbook.SaveAs(filename, XlFileFormat.xlWorkbookNormal) with _ -> ()
workbook.Close false
app.Quit()

release [worksheet; workbook; app]