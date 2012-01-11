#if INTERACTIVE
#r "Mono.Reflection.dll"
#endif

open Microsoft.FSharp.Quotations
open Mono.Reflection
open System.Reflection.Emit
open System.Runtime.CompilerServices

// In Release mode some functions get inlined - in this case
// we can't get its reflected definition from closure

[<ReflectedDefinition>]
[<MethodImpl(MethodImplOptions.NoInlining)>]
let sum a b = a + b

[<ReflectedDefinition>]
[<MethodImpl(MethodImplOptions.NoInlining)>]
let f x = 
    let a = 12. * 4. - float x
    a ** 2.

let funcs: obj list = [ sum; f ]

let getReflectedDefinitions obj =
    let ty = obj.GetType()
    let methods = ty.GetMethods()

    // find closure's invoke
    let invoke = methods |> Array.tryFind (fun mi -> mi.DeclaringType = ty && mi.Name = "Invoke")
    match invoke with
    | Some mi -> // invoke methods do have a body, so we don't check it for null (but e.g. GetType doesn't)
        mi.GetInstructions() 
        |> Seq.tryFind (fun instr -> instr.OpCode = OpCodes.Call)
        |> Option.bind (fun instr -> 
            instr.Operand :?> System.Reflection.MethodInfo // original function info
            |> Expr.TryGetReflectedDefinition)
    | _ -> None

List.map getReflectedDefinitions funcs |> printfn "%A"
