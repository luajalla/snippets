Enjoy [F# Snippets](http://fssnip.net)!


#### Floats in Int Array
A little puzzle: how to print *floats* in the *int array* with printf and get the output like `val arr : int [] = [|1.42; 25.04|]`?

#### Growing Tree Algo for Maze Generation
There are several maze creation [algorithms](http://www.astrolog.org/labyrnth/algrithm.htm). The interesting point about Growing Tree one is that it turns into the others (for example, Recursive Backtracker and Prim's algo) when we choose the next step in different ways. Check it in your browser in [tryfsharp.org](http://www.tryfsharp.org/Tutorials.aspx?view=1&example=http://fssnip.net/raw/7x#)

#### Petrovich
[Petrovich](http://www.dangermouse.net/esoteric/petrovich.html) is more than just a programming language, it is a complete computer operating system and program development environment named after Ivan Petrovich Pavlov. Design Principles: 

* Provide an operating system and computer language that can learn and improve its performance in a natural manner.
* Adapt to user feedback in an intelligent manner.  

#### Get Reflected Definitions
The snippet shows how you can get the reflected definition from a closure object with [Mono.Reflection](https://github.com/jbevain/mono.reflection/)   

#### Transform Expressions into Excel formulae
Sometimes it is extremely useful to check some calculations with Excel. The snippet shows how F# expressions can be transformed into Excel formulae. The data is exported together with the formulae, e.g. a, b and sum function as input sets A1's value to a, B1's to b and C1's formula to "=$A$1+$B$1". [view on fssnip.net](http://fssnip.net/9T)
