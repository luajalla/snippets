// Actually 'float' didn't mean that it was true float type
// The puzzle was to make something look like int =)
// You can simply get [|whatever; you; want|] - and that's int[] too
 
open System
 
type TrickyEnum =
    | ``0.90`` = 0
    | ``1.35`` = 1
    | ``24.081`` = 2
 
let arr = Enum.GetValues typeof<TrickyEnum> :?> int []
// val arr : int [] = [|0.90; 1.35; 24.081|]
 
printfn "%A" arr // prints [|0.90; 1.35; 24.081|]