#r "MathNet.Numerics.dll"
#r "MathNet.Numerics.FSharp.dll"

open MathNet.Numerics.LinearAlgebra.Double
open MathNet.Numerics.LinearAlgebra.Generic

/// inverse matrix given inverse of one-column different matrix
let inv (a: _ Matrix) (aprev: _ Matrix, u: float Vector, l) iter =
    // recompute from scratch, because errors accumulate
    if iter % 20 = 0 then a.Inverse() 
    else
        let ul = u.[l]
        let lrow = aprev.Row l
        Matrix.mapRows (fun i row -> 
            if i = l then row / ul else row - lrow * u.[i] / ul) aprev

type SimplexResult =
    | Success of Vector<float>
    | Error of string

/// Simplex method implementation
let simplexImpl (A: _ Matrix) b (c: _ Vector) (x: _ Vector, Ib: _[], In: _[]) =
    // get Ib, start x
    let cb = Seq.map c.At Ib |> DenseVector.ofSeq
    let m, n = A.RowCount, A.ColumnCount

    // 1. start with basic matrix
    let B = Seq.map A.Column Ib |> DenseMatrix.ofColumns m m
    
    let rec calc (Binv: _ Matrix) iter =
        // 2. reduce costs and check optimality conditions
        let p = cb * Binv * A
        c.MapIndexedInplace (fun i ci -> ci - p.[i])

        match Seq.tryFindIndex (fun i -> c.[i] < 0.) In with
        | Some jind ->
            let j = In.[jind]
            // 3. unboundness check
            let u = Binv * A.Column j
            if Seq.forall (fun ui -> ui <= 0.) u then Error "cost unbounded"
            else
                // 4. improvement
                let l, theta =
                    Seq.mapi (fun i ui -> i, x.[i] / ui) u
                    |> Seq.filter (fun (_, di) -> di > 0.)
                    |> Seq.minBy snd
               
                // 5. update solution
                x.MapIndexedInplace (fun i xi -> if i = l then theta else xi - theta * u.[i])

                // 6. update basis, indices and cost - replace the old basic values with new
                B.SetColumn(l, A.Column j)
                In.[jind] <- Ib.[l]
                Ib.[l] <- j                
                cb.[l] <- c.[j]
               
                let Binv = inv B (Binv, u, l) iter
                calc Binv (iter + 1)
        | _ -> 
            // fill solution vector x0, x1, ..., xn
            let res = DenseVector.zeroCreate n
            Seq.iteri (fun i ib -> res.[ib] <- x.[i]) Ib
            Success res
   
    calc (B.Inverse()) 1


/// naive initialization function - simply set basic x to b
let initSimplex (A0: _ Matrix) (b0: _ Vector) (c0: _ Vector) =
    let m, n = A0.RowCount, A0.ColumnCount
    let n' = m + n
 
    if Seq.exists (fun bi -> bi < 0.) b0 then failwith "b should be > 0"
    
    // A0*x <= b --->  A*x' = b
    let A = DenseMatrix.init m n' (fun i j ->
        if j < n then A0.[i, j]
        elif i = j - n then 1.
        else 0.)
    
    // copy b
    let b = DenseVector.OfVector b0
    // extend c
    let c = DenseVector.init n' (fun i -> if i < n then c0.[i] else 0.)
    // default solution (basis xs)
    let x = DenseVector.OfVector b0
    let In, Ib = [| 0..n-1 |], [| n..n'-1 |]
    
    A, b, c, x, Ib, In


/// Revised Simplex implementation: min cx, Ax <= b, x >= 0, b >= 0
let simplex A0 b0 (c0: _ Vector) =
    let A, b, c, x, Ib, In = initSimplex A0 b0 c0
    match simplexImpl A b c (x, Ib, In) with
    | Success xs -> 
        let x0 = xs.[ .. c0.Count-1]
        let cx = c0 * x0
        Some (x0, cx)
    | _ -> None

module Tests =
    let test1() =  
        let A = matrix [[1.; 1.; 1.; 1.; 0.; 0.; 0.]
                        [1.; 0.; 0.; 0.; 1.; 0.; 0.]
                        [0.; 0.; 1.; 0.; 0.; 1.; 0.]
                        [0.; 3.; 1.; 0.; 0.; 0.; 1.]]

        let c = vector [1.; 5.; -2.; 0.; 0.; 0.; 0.]
        let b = vector [4.; 2.; 3.; 6.]

        // x - start plan, Ib - basic indices, In - nonbasic indices
        let init = vector [2.; 2.; 1.; 4.], [| 0; 2; 5; 6 |], [| 1; 3; 4; |]
        simplexImpl A b c init
        
    (* min x1 + 5*x2 - 2*x3
       subject to
           x1 +  x2 + x3 <= 4
           x1            <= 2
                      x3 <= 3
               3*x2 + x3 <= 6
       x1, x2, x3 >= 0 *)       
    let test2() =
        let A = matrix [[1.; 1.; 1.]
                        [1.; 0.; 0.]
                        [0.; 0.; 1.]
                        [0.; 3.; 1.]]

        let c = vector [1.; 5.; -2.]
        let b = vector [4.; 2.; 3.; 6.]
        
        simplex A b c |> printfn "%A" // [0.0; 0.0; 3.0], -6.0
            

    (* max 18*x1 + 12.5*x2
       subject to
           x1 + x2 <= 20
           x1      <= 12
                x2 <= 16
       x1, x2 >= 0 *)
    let test3() =
        let A = matrix [[ 1.; 1.]
                        [ 1.; 0.]
                        [ 0.; 1.]]
        let b = vector [20.; 12.; 16.]
        let c = vector [-18.; -12.5]
        
        simplex A b c |> printfn "%A" // [12.0; 8.0], -316.0    

    (* max x1 + x2
       subject to
         4*x1 -   x2 <= 8
         2*x1 +   x2 <= 10
         5*x1 - 2*x2 >= -2
       x1, x2 >= 0 *)
    let test4() =
        let A = matrix [[ 4.; -1.]
                        [ 2.;  1.]
                        [-5.;  2.]]
        let b = vector [8.; 10.; 2.]
        let c = vector [-1.; -1.]
        
        simplex A b c |> printfn "%A" // [3.0; 4.0], -7.0    
