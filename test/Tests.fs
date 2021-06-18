namespace FSharp.Data.Mutator.Tests
open System
open Xunit
open Microsoft.FSharp.Linq.RuntimeHelpers
open FSharp.Data.Mutator
open FSharp.Data

module Tests =

    [<Literal>]
    let testJson = 
        """
        {
            "name" : "name",
            "friends" : [
               { "name" : "name", "age" : 0, "phone" : "phone" },
               { "name" : "name", "age" : 0, "phone" : "phone" }
            ],
            "nested" : {
                 "someObj" : {
                       "more" : {
                           "property" : 0.0,
                           "name" : "x"
                       }
                 }
            }
        }
        """

    [<Literal>]
    let test2Json =
        """
        { "object" : {
            "nested" : 1,
            "unknown" : null,
            "second" : {
                "name" : "john",
                "third" : [
                    {
                        "fouth":"hi"
                    }
                ]
            },
            "date" : "2012-04-23T18:25:43.511Z",
            "guid" : "ad8d6b87-833f-4b6e-98bc-52b1cfbc814e"
        }}
        """

    type Provided = JsonProvider<testJson>
    type NestedProvider = JsonProvider<test2Json>

    let sample = Provided.GetSample()

    type R = Provided.Root

    [<Fact>]
    let ``Match expression left right with string``() =

           let expr = <@ fun (o : R) -> o.Name = "HELLO" @>

           let lr = 
               expr 
               |> LeafExpressionConverter.QuotationToExpression
               |> getLR
           
           Assert.NotEmpty(lr)
           Assert.True(lr.Length = 2)

    [<Fact>]
    let ``Match expression left right with complex obj``() =

              let expr = <@ fun (o : R) -> 
                  o.Nested.SomeObj = new Provided.SomeObj(new Provided.More(1,"hello")) @>

              let lr = 
                  expr 
                  |> LeafExpressionConverter.QuotationToExpression
                  |> getLR
              
              Assert.NotEmpty(lr)
              Assert.True(lr.Length = 2)
       
    

    [<Fact>]
    let ``IJsonDocument extension update leaf with value``() =

        let resultJson = sample.Change(<@ fun o  -> o.Nested.SomeObj.More = new Provided.More(1,"hello")@>)
            
        Assert.Equal(1, resultJson.Nested.SomeObj.More.Property)
        Assert.Equal("hello", resultJson.Nested.SomeObj.More.Name)

    [<Fact>]
    let ``IJsonDocument multiple updates``() =

        let result = 
            sample
            |> Change <@ fun o -> o.Name = "A" @>
            |> Change <@ fun o -> o.Name = "B" @>
            |> Change <@ fun o -> o.Name = "C" @>

        Assert.Equal("C", result.Name)



    [<Fact>]
    let ``Update leaf nested`` () =
        
        let newItems = sample.Friends |> Array.map (fun i ->
            i.Change(fun z -> z.Name = "UPDATED")
        )

        let result = new R(sample.Name, newItems, sample.Nested)
            
        Assert.Equal("UPDATED", result.Friends.[0].Name)
        Assert.Equal("UPDATED", result.Friends.[1].Name)

    
    [<Fact>]
    let ``IJsonDocument extension update leaf with value nested``() =

        let resultJson = sample.Change (fun o -> o.Friends.[1].Phone = "808")
            
        Assert.Equal("808", resultJson.Friends.[1].Phone)

    [<Fact>]
    let ``Other Tests To Separate And Adjust``() =

        let t = NestedProvider.GetSample()
        
        let testMe x =
           $"HELLO {x + 5}"
        
        let now = DateTimeOffset.UtcNow

        let guid = Guid.NewGuid()

        let changed =
            NestedProvider.GetSample()
            |> Change <@ fun x ->  x.Object.Date = now @>
            |> Change <@ fun x ->  x.Object.Guid = Guid.NewGuid() @>
            |> Change <@ fun x ->  x.Object.Guid = guid @>
            |> Change <@ fun x -> 
                x.Object.Second = NestedProvider.Second("wacko", 
                    [|  NestedProvider.Third("he") ; NestedProvider.Third("ho") |])@>
            |> Change <@ fun x ->  x.Object.Unknown.JsonValue = JsonValue.String("hi") @>
            |> Change <@ fun x ->  x.Object.Second.Third = [|  NestedProvider.Third($"{testMe 4}") |] @>
        
        Assert.Equal("wacko", changed.Object.Second.Name)
        Assert.Equal(1, changed.Object.Second.Third.Length)
        Assert.Equal(now, changed.Object.Date)
        Assert.Equal(guid, changed.Object.Guid)
        Assert.Equal("hi" |> JsonValue.String, changed.Object.Unknown.JsonValue)
        Assert.Equal("HELLO 9", (changed.Object.Second.Third.[0].JsonValue |> NestedProvider.Third).Fouth)
    


