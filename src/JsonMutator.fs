namespace FSharp.Data.Mutator

open System
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Linq.Expressions
open Microsoft.FSharp.Linq.RuntimeHelpers
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection
open System.Collections.Generic
open System.Runtime.CompilerServices
open FSharp.Data
open FSharp.Data.Runtime.BaseTypes

[<AutoOpen>]
module JsonMutator =

    // -----------------------------
    // JsonNode <-> FSharp.Data.JsonValue
    // -----------------------------

    type JsonNode with
        member this.JsonValue() =
            this.ToJsonString() |> JsonValue.Parse

    type JsonValue with
        member this.JsonNode() =
            this.ToString()
            |> JsonNode.Parse

    // -----------------------------
    // "With" helpers (mutation)
    // -----------------------------

    type JsonNode with
        member this.With (mutatorFunc: JsonNode -> 'a) =
            this |> fun n -> mutatorFunc n |> ignore; n

    type JsonValue with
        member this.With (mutatorFunc: JsonNode -> 'a) =
            this.JsonNode().With(mutatorFunc).JsonValue()

    // -----------------------------
    // Expression helpers
    // -----------------------------

    let getLR expr =
        let rec getLeftRight (expr: Expression) r =
            match expr with
            | :? MethodCallExpression as mc when mc.Arguments.Count > 0 ->
                getLeftRight (mc.Arguments.[0]) r
            | :? LambdaExpression as l ->
                getLeftRight l.Body r
            | :? BinaryExpression as be ->
                getLeftRight be.Right [ be.Left; be.Right ]
            | _ -> r
        getLeftRight expr []

    // -----------------------------
    // JsonPath-like selection for JsonNode
    // -----------------------------

    let private trySelectPath (root: JsonNode) (path: string) : JsonNode option =
        if String.IsNullOrWhiteSpace path then
            Some root
        else
            let parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries)

            let parsePart (p: string) =
                let idxStart = p.IndexOf('[')
                if idxStart >= 0 then
                    let idxEnd = p.IndexOf(']', idxStart + 1)
                    let name = p.Substring(0, idxStart)
                    let idx = p.Substring(idxStart + 1, idxEnd - idxStart - 1) |> int
                    name, Some idx
                else
                    p, None

            let rec loop (node: JsonNode) i =
                if i = parts.Length then
                    Some node
                else
                    let name, idxOpt = parsePart parts.[i]

                    match node : JsonNode with
                    | :? JsonObject as o ->
                        let mutable outNode: JsonNode = null
                        match o.TryGetPropertyValue(name, &outNode) with
                        | true ->
                            match idxOpt, outNode with
                            | Some idx, (:? JsonArray as arr) when idx >= 0 && idx < arr.Count ->
                                loop arr.[idx] (i + 1)
                            | None, _ ->
                                loop outNode (i + 1)
                            | _ ->
                                None
                        | false ->
                            None

                    | :? JsonArray as arr ->
                        match idxOpt with
                        | Some idx when idx >= 0 && idx < arr.Count ->
                            loop arr.[idx] (i + 1)
                        | _ ->
                            None

                    | _ ->
                        None

            loop root 0

    // -----------------------------
    // Option detection and conversion
    // -----------------------------

    let private isOptionType (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    let private toJsonNodeFromObj (o: obj) : JsonNode =
        match o with
        | null ->
            JsonValue.Create(null) :> JsonNode

        | _ when isOptionType (o.GetType()) ->
            let case, fields = FSharpValue.GetUnionFields(o, o.GetType())
            match case.Name, fields with
            | "Some", [| v |] ->
                JsonValue.Create(v) :> JsonNode
            | "None", _ ->
                JsonValue.Create(null) :> JsonNode
            | _ ->
                JsonValue.Create(null) :> JsonNode

        | _ ->
            JsonValue.Create(o) :> JsonNode

    // -----------------------------
    // UpdateLeaf using System.Text.Json.Nodes
    // -----------------------------

    let UpdateLeaf<'a when 'a :> IJsonDocument>
        (updateAction: Expr<'a -> bool>)
        (jsonValue: JsonValue)
        =
        let expression =
            updateAction
            |> LeafExpressionConverter.QuotationToExpression

        let binomialResult =
            expression
            |> getLR

        let jsonNodeToSet: JsonNode =
            match binomialResult with
            | [ _l; r ] ->
                let tName = r.Type.Name.ToLower()
                match tName with
                | "jsonvalue" ->
                    let lambda = r.Reduce()
                    let rVal = Expression.Lambda(lambda).Compile().DynamicInvoke()
                    (rVal :?> JsonValue).JsonNode()

                | "ijsondocument" ->
                    let lambda = r.Reduce()
                    let rVal = Expression.Lambda(lambda).Compile().DynamicInvoke()
                    (rVal :?> IJsonDocument).JsonValue.JsonNode()

                | "ijsondocument[]" ->
                    let lambda = r.Reduce()
                    let rVal = Expression.Lambda(lambda).Compile().DynamicInvoke()
                    let nodes =
                        (rVal :?> IJsonDocument[])
                        |> Array.map (fun x -> x.JsonValue.JsonNode())
                    let arr = JsonArray()
                    nodes |> Array.iter arr.Add
                    arr :> JsonNode

                | _ ->
                    let lambda = r.Reduce()
                    let rVal = Expression.Lambda(lambda).Compile().DynamicInvoke()
                    toJsonNodeFromObj rVal

            | _ ->
                JsonValue.Create(null) :> JsonNode

        let left =
            match binomialResult with
            | [ l; _r ] -> l
            | _ -> expression

        let cleanedStr = Regex.Replace(left.ToString(), @"\t|\n|\r", "")

        let invertedCalls =
            Regex.Matches(cleanedStr, "\,\s+(?<Prop>(\")?(\w|\$)+(?<IsDigit>\")?)\)")
            |> Seq.map (fun m ->
                let prop = m.Groups.["Prop"].Value.Replace("\"", "")
                let isDigit = String.IsNullOrEmpty(m.Groups.["IsDigit"].Value)
                prop, isDigit)
            |> Seq.fold
                (fun acc (prop, isDigit) ->
                    match isDigit with
                    | true ->
                        let q = acc |> Seq.ofList |> Queue
                        let p = q.Dequeue()
                        $"{p}.[{prop}]" :: (q |> List.ofSeq)
                    | false ->
                        prop :: acc)
                []
            |> List.ofSeq

        let key = invertedCalls.[0]
        let jsonPath = String.Join(".", invertedCalls |> Seq.skip 1 |> Seq.rev)

        jsonValue.With(fun root ->
            match trySelectPath root jsonPath with
            | Some target ->
                match target : JsonNode with
                | :? JsonObject as o ->
                    o.[key] <- jsonNodeToSet

                | :? JsonArray as arr ->
                    match Int32.TryParse key with
                    | true, idx when idx >= 0 && idx < arr.Count ->
                        arr.[idx] <- jsonNodeToSet
                    | _ -> ()

                | _ -> ()

            | None -> ()
        )

    // -----------------------------
    // Change helpers (public API)
    // -----------------------------

    let Change<'a when 'a :> IJsonDocument>
        (updateAction: Expr<'a -> bool>)
        (jsonDocument: 'a)
        =
        UpdateLeaf updateAction jsonDocument.JsonValue
        |> fun x -> JsonDocument.Create(x, "") :?> 'a

    [<Extension>]
    type ExtensionMethod() =
        [<Extension>]
        static member Change<'a when 'a :> IJsonDocument>
            (
                this: 'a,
                [<ReflectedDefinition>] updateAction: Expr<'a -> bool>
            ) =
            this.JsonValue
            |> UpdateLeaf updateAction
            |> fun x -> JsonDocument.Create(x, "") :?> 'a
