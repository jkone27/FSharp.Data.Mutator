#i "nuget:C:\TestNugetPackages" //change with your local feed
#r "nuget:FSharp.Data.Mutator,0.1.0-beta"


open FSharp.Data
open FSharp.Data.Mutator

[<Literal>]
let jsonTest =
    """
    {
        "name":"hey"
    }
    """

type TestJsonProvided = JsonProvider<jsonTest>

TestJsonProvided.GetSample().Change(fun x -> x.Name = "bye")