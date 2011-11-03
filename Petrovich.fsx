﻿open System

(* Petrovich provides two methods of influencing its behaviour: rewards and punishments. 
Whenever Petrovich does something the user doesn't approve of, the user can punish it. 
Conversely, whenever Petrovich does something useful, the user can reward it. 
Petrovich then adapts its behaviour to avoid punishment and enjoy more rewards. *)
type Action = 
    | DoSmth                    //Causes Petrovich to do something. 
    | DoSmthWithFile of string  //Causes Petrovich to do something using the named file. 
    | Punish                    //Punishes Petrovich. 
    | Reward                    //Rewards Petrovich. 
    | Exit                      

let print a = a >> printfn "%A"

// Do Something Actions Realization
let beep() = Console.Beep()
let get100Numbers() = [1..100]
let getDate() = DateTime.Now
let getTimeZoneInfo() = TimeZoneInfo.Local
let sleep() = printfn "sleeping..."; Threading.Thread.Sleep 10000
let sortArray() = 
    let r = new Random()
    let array = Array.init 10 r.Next
    printfn "sorting: %A" array
    Array.sort array
let doSmthActions = [
    beep
    print get100Numbers
    print getDate 
    print getTimeZoneInfo 
    print sortArray
    sleep
]

// Do Something With File Actions Realization
let doIfExists action fName = 
    if IO.File.Exists fName then action fName
    else 
        IO.File.WriteAllText(fName, "new file")
        printfn "file was created"

let getCreationTime = doIfExists <| print IO.File.GetCreationTime
let getPath = IO.Path.GetFullPath
let read = doIfExists <| print IO.File.ReadAllText

let delete = doIfExists (fun fName -> 
    IO.File.Delete fName 
    printfn "file was deleted")

let writeGuid = doIfExists (fun fName -> 
    IO.File.WriteAllText(fName, Guid.NewGuid().ToString())
    printfn "guid was saved")

//Attention! Don't try this at home, especially with some important files! =)
let doSmthWithFileActions = [
    getCreationTime
    print getPath
    writeGuid
    delete
    read
]

type DecisionList<'a>(list: 'a list) =
    let variants = list.Length / 3 
    let decisions = new Collections.Generic.List<'a>(list)
    let random = new Random()
    let mutable last: Option<int> = None

    member i.Choose() =
        let index = random.Next(0, variants)
        last <- Some index
        decisions.[index]
    member i.Reward() =
        match last with
        | None  -> ()
        | Some index -> 
            let decision = decisions.[index]
            decisions.RemoveAt index
            decisions.Insert (random.Next variants, decision)
            last <- None
    member i.Punish() =
        match last with
        | None -> ()
        | Some index -> 
            let decision = decisions.[index]
            decisions.RemoveAt index
            let newIndex = index + random.Next(variants, decisions.Count) 
            if newIndex < decisions.Count then
                decisions.Insert (newIndex, decision)
            else 
                decisions.Add decision  
            last <- None                 

type OS() =
    let doSmth = new DecisionList<_>(doSmthActions)
    let doSmthWithFile = new DecisionList<_>(doSmthWithFileActions)

    let printLine = printfn "Petrovich> %s"
    (* MailboxProcessor with 2 states: 
        - 'command' to make it do something
        - 'response' to formulate a reflex *)
    let core =
        MailboxProcessor.Start(fun inbox ->
            let rec command() =
                async { let! msg = inbox.Receive()
                    match msg with
                    | DoSmth -> 
                        printLine "do something"
                        doSmth.Choose()()
                        return! response doSmth.Punish doSmth.Reward
                    | DoSmthWithFile fileName ->
                        printLine <| "do something with " + fileName
                        doSmthWithFile.Choose() fileName
                        return! response doSmthWithFile.Punish doSmthWithFile.Reward
                    | Exit ->
                        printLine "exit" 
                        return ()
                    | _ -> return! command()
                }
            and response (*on punish*)p (*on reward*)r =
                async { let! msg = inbox.Receive()
                    match msg with
                    | Punish ->
                        printLine "punish"; p()
                        return! command()
                    | Reward ->
                        printLine "reward"; r()
                        return! command()
                    | Exit ->
                        printLine "exit" 
                        return ()
                    | _ -> return! response p r
                }
            command())

    member i.DoSomething() = core.Post DoSmth
    member i.DoSomethingWithFile fName = core.Post << DoSmthWithFile <| fName
    member i.Reward() = core.Post Reward
    member i.Punish() = core.Post Punish
    member i.Exit() = core.Post Exit

//time to test:
let petrovich = new OS()
petrovich.DoSomething()
petrovich.DoSomethingWithFile "test.txt"
petrovich.Reward()
petrovich.Punish()
petrovich.Exit()