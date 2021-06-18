namespace FSharp.Data.Mutator

open Newtonsoft.Json.Linq
open Microsoft.FSharp.Quotations
open FSharp.Data
open System.Text.RegularExpressions
open System.Linq.Expressions
open Microsoft.FSharp.Linq.RuntimeHelpers
open System.Collections.Generic
open System
open FSharp.Data.Runtime.BaseTypes
open System.Runtime.CompilerServices


[<AutoOpen>]
module JsonMutator =
    
    type JToken with
        member this.JsonValue() =
            this.ToString() |> JsonValue.Parse
    
    type JToken with 
        member this.With (mutatorFunc: JToken -> 'a) =
            this |> fun y -> mutatorFunc(y) |> ignore; y
    
    type JsonValue with 
        member this.JToken() =
            this.ToString()
            |> JToken.Parse
    
    type JsonValue with 
        member this.With (mutatorFunc: JToken -> 'a) =
            this.JToken().With(mutatorFunc).JsonValue()

    let getLR expr =
        let rec getLeftRight (expr : Expression) r =
            match expr with
            | :? MethodCallExpression as mc when mc.Arguments.Count > 0 -> 
                getLeftRight (mc.Arguments.[0]) r
            | :? LambdaExpression as l -> 
                getLeftRight l.Body r
            | :? BinaryExpression as be -> 
                getLeftRight be.Right [be.Left; be.Right]
            |_ -> r
        getLeftRight expr []
    
    let inline UpdateLeaf<'a when 'a :> IJsonDocument>(updateAction: Expr<('a -> bool)>) (jsonValue: JsonValue) =
      
          let expression = 
              updateAction 
              |> LeafExpressionConverter.QuotationToExpression
      
          let binomialResult =
              expression
              |> getLR

          // todo if not primitive, turn to JToken
          let jtoken = 
              match binomialResult with
              [l;r] ->
                let t = r.Type.Name.ToLower()
                match t with
                | "jsonvalue" -> 
                    let lambda = r.Reduce()
                    let r = Expression.Lambda(lambda).Compile().DynamicInvoke()
                    (r :?> JsonValue).JToken()
                | "ijsondocument" -> 
                    let lambda = r.Reduce()
                    let r = Expression.Lambda(lambda).Compile().DynamicInvoke()
                    (r :?> IJsonDocument).JsonValue.JToken()
                | "ijsondocument[]" -> 
                    let lambda = r.Reduce()
                    let r = Expression.Lambda(lambda).Compile().DynamicInvoke()
                    let stringList =
                        (r :?> IJsonDocument[]) 
                        |> Array.map (fun x -> x.JsonValue.JToken())
                               
                    let jarrayString = String.Join(",", stringList)
                               
                    $"[{jarrayString}]"
                    |> JArray.Parse
                    :> JToken
                |_ ->  
                    let tOption = typeof<option<_>>.GetGenericTypeDefinition()
                    let lambda = r.Reduce()
                    let r = Expression.Lambda(lambda).Compile().DynamicInvoke()
                    match r with
                    | null -> JValue.CreateNull() :> JToken
                    |_ as o when r.GetType().IsGenericType && r.GetType().GetGenericTypeDefinition() = tOption ->
                        let strOption = Newtonsoft.Json.JsonConvert.SerializeObject(o)
                        let opt = Newtonsoft.Json.JsonConvert.DeserializeObject<Option<Object>>(strOption)
                        match opt with
                        |Some(v) -> 
                            JValue(v) :> JToken
                        |None -> 
                            JValue.CreateNull() :> JToken
                             
                    |_ -> JValue(r) :> JToken

          let left = 
              match binomialResult with
              |[l;r] -> l
              |_ -> expression
            
          let cleanedStr = Regex.Replace(left.ToString(), @"\t|\n|\r", "")

          let invertedCalls = 
              Regex.Matches(cleanedStr, "\,\s+(?<Prop>(\")?(\w|\$)+(?<IsDigit>\")?)\)")
              |> Seq.map (fun m -> (m.Groups.["Prop"].Value.Replace("\"",""), String.IsNullOrEmpty(m.Groups.["IsDigit"].Value)))
              |> Seq.fold (fun acc next -> 
                  let prop,isDigit = next
                  match isDigit with
                  |true ->
                      let q = acc |> Seq.ofList |> Queue
                      let p = q.Dequeue()
                      $"{p}.[{prop}]" :: (q |> List.ofSeq)
                  |false -> 
                      prop :: acc
              ) []
 

          let key = invertedCalls.[0]
          let jsonPath = System.String.Join(".", invertedCalls |> Seq.skip 1 |> Seq.rev)
      
          jsonValue.With(fun x -> x.SelectToken(jsonPath).[key] <- jtoken)
    
    let inline Change<'a when 'a :> IJsonDocument>(updateAction: Expr<('a -> bool)>) (jsonDocument : 'a) =
        UpdateLeaf updateAction jsonDocument.JsonValue
        |> fun x -> JsonDocument.Create(x,"") :?> 'a

    [<Extension>]
    type ExtensionMethod() =
        [<Extension>]
        static member inline Change<'a when 'a :> IJsonDocument>(this: 'a,[<ReflectedDefinition>] updateAction: Expr<('a -> bool)>) =
            this.JsonValue
            |> UpdateLeaf updateAction 
            |> fun x -> JsonDocument.Create(x,"") :?> 'a

