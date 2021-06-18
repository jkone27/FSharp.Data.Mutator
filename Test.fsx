//#i "nuget:C:\TestNugetPackages" //change with your local feed
#r "nuget:FSharp.Data.Mutator,0.1.0-beta"


open FSharp.Data
open FSharp.Data.Mutator

[<Literal>]
let jsonTest =
    """
       {
           "name":"hey",
           "nested": {
               "another" : "val"
           },
           "items" : [
               { "first" : 1 }
           ] 
       }
    """

type TestJsonProvided = JsonProvider<jsonTest>

TestJsonProvided.GetSample().Change(fun x -> x.Name = "bye")

TestJsonProvided.GetSample()
|> Change <@ fun x -> x.Name = "one" @>
|> Change <@ fun x -> x.Nested.Another = "two" @>
|> Change <@ fun x -> x.Items.[0].First = 500 @>