﻿// Try it with tryfsharp.org!
// http://www.tryfsharp.org/Tutorials.aspx?view=1&example=http://fssnip.net/raw/7x#

open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Shapes
open System.Collections.Generic

let directions = [|
    0, 1   // down
    1, 0   // right
    0, -1  // up
    -1, 0  // left
|]

type CellType = Free | Wall

/// Several heuristics for choosing the next cell
type GrowMethod = 
    | RecursiveBacktracker 
    | Prim 
    | ChooseTheOldest
    with override this.ToString() = 
                    match this with 
                    | RecursiveBacktracker -> "Recursive Backtracker"
                    | Prim -> "Prim"
                    | ChooseTheOldest -> "Choose the Oldest"

let createMaze xMax yMax =
    let maze = Array2D.create xMax yMax Wall
    // Check if (x, y) are valid coordinates
    let inline inMaze x y = 0 <= x && x < xMax && 0 <= y && y < yMax

    // The wall at (x, y) between current cell and another can be removed 
    // if all it's neighbors are walls too (we leave a border of walls)
    let canRemoveWall x y = 
        let dirs =
            directions |> Array.sumBy (fun (dx, dy) ->
                let x', y' = x + dx, y + dy
                if inMaze x' y' && maze.[x', y'] = Wall then 1 else 0)
        dirs = 3

    // Check if a cell is not free yet and the wall can be removed
    let getPossibleDirections (x, y) = async {
        return directions |> Array.filter (fun (dx, dy) ->
            let x', y' = x + dx, y + dy
            inMaze x' y' && maze.[x', y'] = Wall && canRemoveWall x' y')}
    maze, getPossibleDirections

let inline map f (x, y) = f x, f y

type MazeControl() as this =
    inherit UserControl()

    let pause() = Async.Sleep 25

    let canvas = Canvas(Background = SolidColorBrush Colors.Blue)
    do this.Content <- canvas

    // Create a rectangle at the cell: current - red, others - white
    let createRectangle (cell, current) =
        let color, offset, size = if current then Colors.Red, 2., 6. else Colors.White, 0.5, 9. 
        let x, y = map (fun x -> float x * 10. + offset) cell

        let rect = Rectangle(Width = size, Height = size, Fill = SolidColorBrush color)
        rect.SetValue(Canvas.LeftProperty, x)
        rect.SetValue(Canvas.TopProperty, y)
        rect (*[/omit]*)

    // Fill a cell with corresponding color
    let fill = createRectangle >> canvas.Children.Add
 
    
    let drawMaze xMax yMax growMethod =
        this.Width <- float xMax * 10.
        this.Height <- float yMax * 10.
        let rand = System.Random() (*[/omit]*)
        let maze, getPossibleDirections = createMaze xMax yMax
        // List of the cells to choose from
        let cells = new ResizeArray<_>()
        
        // To get the Recursive Backtracker we choose the most recent cell
        // For Prim - the random one
        // And the third one - the oldest
        let chooseNext() = 
            let ind = 
                match growMethod with
                | RecursiveBacktracker -> cells.Count - 1
                | Prim -> rand.Next cells.Count
                | _ -> 0
            cells.[ind]

        // Choose a start point
        let sx, sy = rand.Next (1, xMax-1), rand.Next (1, yMax-1)
        maze.[sx, sy] <- Free
        cells.Add (sx, sy)

        // Draw the maze
        let rec run() = async {
            if cells.Count = 0 then () // If there're no cells - finish
            else
                // Go to the next cell and draw it as current
                let cell = chooseNext()
                fill (cell, true) 
                do! pause()

                let! possibleDirections = getPossibleDirections cell
                match possibleDirections.Length with
                | 0 -> cells.Remove cell |> ignore  // There's no way to go - remove it
                | len -> 
                    // Randomly choose a direction
                    let dx, dy = possibleDirections.[rand.Next len]
                    let x, y = fst cell + dx, snd cell + dy
                    maze.[x, y] <- Free
                    // Add to list as a candidate for a futures growth
                    cells.Add (x, y)
               
                fill (cell, false) // It's not current any more
                do! run()  
        }
        run()
    
    /// Drawing a 21x21 maze with a specified method
    member this.DrawMaze growMethod =
        Async.CancelDefaultToken() // Cancel drawing of the previous maze
        canvas.Children.Clear()  
        drawMaze 21 21 growMethod |> Async.StartImmediate

open Microsoft.TryFSharp
App.Dispatch (fun() -> 
    App.Console.ClearCanvas()
    let canvas = App.Console.Canvas
    let maze = MazeControl()
    let sp = StackPanel()
    let cb = ComboBox(
              ItemsSource = [RecursiveBacktracker; Prim; ChooseTheOldest],
              Margin = Thickness(0.,0.,0.,5.), 
              HorizontalAlignment = HorizontalAlignment.Center,
              MinWidth = 150.) 
    cb.SelectionChanged.AddHandler(fun _ _ -> maze.DrawMaze (cb.SelectedItem :?> GrowMethod))
    sp.Children.Add cb
    sp.Children.Add maze
    canvas.Children.Add sp
    App.Console.CanvasPosition <- CanvasPosition.Right
    cb.Focus() |> ignore
    cb.SelectedIndex <- 0
)