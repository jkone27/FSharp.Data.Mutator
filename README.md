# [FSharp.Data.Mutator](https://www.nuget.org/packages/FSharp.Data.Mutator)

<img src="https://github.com/jkone27/FSharp.Data.Mutator/blob/main/icon.png?raw=true" />

Enables to create copies (similar to lenses) to generated FSharp.Data types (json only for now),
The library now depends both on FSharp.Data and Newtonsoft.Json as dependencies, but can be improved.

A [medium article](https://jkone27-3876.medium.com/fsharp-data-mutator-66550bb6a2cc) about it.

## Usage

```fsharp
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
```

Or for a chain of calls (more functional-way)

```fsharp

TestJsonProvided.GetSample()
|> Change <@ fun x -> x.Name = "one" @>
|> Change <@ fun x -> x.Nested.Another = "two" @>
|> Change <@ fun x -> x.Items.[0].First = 500 @>

```
and the output should be

```
val it : JsonProvider<...>.Root =
  {
  "name": "one",
  "nested": {
    "another": "two"
  },
  "items": [
    {
      "first": 500
    }
  ]
}
```

Have fun!
