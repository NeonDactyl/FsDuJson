namespace FsDuJson

open System
open System.Collections.Concurrent
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.FSharp.Reflection

/// <summary>
/// A data structure to cache reflection information for a Discriminated Union case.
/// This avoids repeated, expensive reflection calls during serialization/deserialization.
/// </summary>
type UnionCaseInfoCache =
    { CaseInfo: UnionCaseInfo
      FieldInfos: PropertyInfo[] }

/// <summary>
/// A generic custom JsonConverter for F# discriminated unions that handles
/// single-field, multi-field, and nullary cases. This converter serializes
/// DUs into a JSON object with a single key representing the case name.
/// Nullary cases are serialized as strings. Multi-field cases are
/// serialized as a JSON array, with named fields appearing as objects
/// within the array. This version is optimized with a cache.
/// </summary>
[<Sealed>]
type FSharpUnionConverter<'T>(caseCache: ConcurrentDictionary<string, UnionCaseInfoCache>) =
    inherit JsonConverter<'T>()

    /// <summary>
    /// Determines if this converter can handle the given type.
    /// </summary>
    override _.CanConvert(typeToConvert: Type) =
        FSharpType.IsUnion typeToConvert

    /// <summary>
    /// Deserializes a discriminated union from JSON.
    /// </summary>
    override _.Read(reader: byref<Utf8JsonReader>, typeToConvert: Type, options: JsonSerializerOptions) =
        if reader.TokenType = JsonTokenType.StartObject then
            reader.Read() |> ignore

            if reader.TokenType <> JsonTokenType.PropertyName then
                raise (JsonException("Expected PropertyName"))

            let caseName = reader.GetString()
            let cachedInfo = 
                match caseCache.TryGetValue(caseName) with
                | true, cache -> cache
                | _ -> raise (JsonException($"Unknown case: {caseName}"))

            reader.Read() |> ignore

            let values =
                match cachedInfo.FieldInfos.Length with
                | 0 -> 
                    if reader.TokenType <> JsonTokenType.Null then
                        raise (JsonException("Expected null for nullary case"))
                    [||]
                | 1 ->
                    let value = JsonSerializer.Deserialize(&reader, cachedInfo.FieldInfos[0].PropertyType, options)
                    [| value |]
                | _ ->
                    if reader.TokenType <> JsonTokenType.StartArray then
                        raise (JsonException("Expected StartArray for multi-field DU"))
                    
                    let values = Array.zeroCreate cachedInfo.FieldInfos.Length
                    for i in 0 .. cachedInfo.FieldInfos.Length - 1 do
                        reader.Read() |> ignore
                        let fieldInfo = cachedInfo.FieldInfos[i]
                        let value = 
                            if not (fieldInfo.Name.StartsWith("Item")) && reader.TokenType = JsonTokenType.StartObject then
                                reader.Read() |> ignore
                                if reader.TokenType <> JsonTokenType.PropertyName then
                                    raise (JsonException("Expected PropertyName for named field"))
                                let propName = reader.GetString()
                                if propName <> fieldInfo.Name then
                                    raise (JsonException($"Expected property name '{fieldInfo.Name}' but got '{propName}'"))
                                reader.Read() |> ignore
                                let deserializedValue = JsonSerializer.Deserialize(&reader, fieldInfo.PropertyType, options)
                                reader.Read() |> ignore
                                deserializedValue
                            else
                                JsonSerializer.Deserialize(&reader, fieldInfo.PropertyType, options)
                        values[i] <- value
                    
                    reader.Read() |> ignore
                    values

            reader.Read() |> ignore

            FSharpValue.MakeUnion(cachedInfo.CaseInfo, values) :?> 'T
        elif reader.TokenType = JsonTokenType.String then
            let caseName = reader.GetString()
            let cachedInfo = 
                match caseCache.TryGetValue(caseName) with
                | true, cache -> cache
                | _ -> raise (JsonException($"Unknown nullary case: {caseName}"))

            if cachedInfo.FieldInfos.Length <> 0 then
                raise (JsonException($"Case '{caseName}' is not a nullary case."))
            FSharpValue.MakeUnion(cachedInfo.CaseInfo, [||]) :?> 'T
        else
            raise (JsonException("Expected StartObject or String for discriminated union."))

    /// <summary>
    /// Serializes a discriminated union to JSON.
    /// </summary>
    override _.Write(writer: Utf8JsonWriter, value: 'T, options: JsonSerializerOptions) =
        let caseInfo, fields = FSharpValue.GetUnionFields(value, typeof<'T>)
        
        match fields with
        | [||] ->
            writer.WriteStringValue(caseInfo.Name)
        | _ ->
            let fieldInfos = 
                match caseCache.TryGetValue(caseInfo.Name) with
                | true, cache -> cache.FieldInfos
                | _ -> [||] // Should not happen

            writer.WriteStartObject()
            writer.WritePropertyName(caseInfo.Name)

            match fields with
            | [| single |] ->
                JsonSerializer.Serialize(writer, single, single.GetType(), options)
            | multiple ->
                writer.WriteStartArray()
                
                for i in 0 .. multiple.Length - 1 do
                    let fieldInfo = fieldInfos[i]
                    let fieldValue = multiple[i]

                    if not (fieldInfo.Name.StartsWith("Item")) then
                        writer.WriteStartObject()
                        writer.WritePropertyName(fieldInfo.Name)
                        JsonSerializer.Serialize(writer, fieldValue, fieldValue.GetType(), options)
                        writer.WriteEndObject()
                    else
                        JsonSerializer.Serialize(writer, fieldValue, fieldValue.GetType(), options)
                writer.WriteEndArray()

            writer.WriteEndObject()

/// <summary>
/// A factory that automatically creates a custom converter for any F#
/// discriminated union type. This allows for a single registration point
/// for the library. This factory implements a memoization cache to
/// store reflection data for improved performance.
/// </summary>
[<Sealed>]
type FSharpUnionConverterFactory() =
    inherit JsonConverterFactory()

    // Thread-safe cache to store reflection information for each DU type
    let typeCache = ConcurrentDictionary<Type, ConcurrentDictionary<string, UnionCaseInfoCache>>()

    /// <summary>
    /// Determines if the factory can create a converter for the given type.
    /// </summary>
    override this.CanConvert(typeToConvert: Type) =
        FSharpType.IsUnion typeToConvert

    /// <summary>
    /// Creates a generic instance of the FSharpUnionConverter for the given type.
    /// This method also populates the cache for the type if it doesn't exist.
    /// </summary>
    override this.CreateConverter(typeToConvert: Type, options: JsonSerializerOptions) =
        let duCaseCache = 
            typeCache.GetOrAdd(typeToConvert, fun _ ->
                let cache = ConcurrentDictionary<string, UnionCaseInfoCache>()
                let unionCases = FSharpType.GetUnionCases(typeToConvert)
                for caseInfo in unionCases do
                    let fieldInfos = caseInfo.GetFields()
                    cache.TryAdd(caseInfo.Name, { CaseInfo = caseInfo; FieldInfos = fieldInfos }) |> ignore
                cache
            )

        let converterType = typedefof<FSharpUnionConverter<_>>
        let genericConverterType = converterType.MakeGenericType(typeToConvert)
        Activator.CreateInstance(genericConverterType, duCaseCache) :?> JsonConverter

/// <summary>
/// A static module for providing default serialization options.
/// </summary>
[<AutoOpen>]
module FSharpUnionJson =
    /// <summary>
    /// Returns a default configured JsonSerializerOptions object with the
    /// FSharpUnionConverterFactory already added.
    /// </summary>
    let DefaultFsDuJsonOptions() =
        let options = JsonSerializerOptions()
        options.Converters.Add(FSharpUnionConverterFactory())
        options.WriteIndented <- true
        options