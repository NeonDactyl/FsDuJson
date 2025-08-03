# FsDuJson: A `System.Text.Json` Converter for F# Discriminated Unions

FsDuJson is a custom `System.Text.Json.Serialization.JsonConverterFactory` designed to provide seamless and performant serialization and deserialization of F# discriminated unions (DUs). This library addresses the limitations of the default `System.Text.Json` behavior, enabling you to represent F# DUs in a clean and idiomatic JSON format.

## Features

- **Handles all DU types:** Supports nullary (no-field), single-field, and multi-field discriminated unions.
- **Optimized for performance:** Utilizes a memoization cache to store reflection data, ensuring that expensive reflection lookups are only performed once per type. This makes it highly efficient for long-running, high-throughput applications.
- **Idempotent Serialization:** Serializes DUs into a JSON object where the key is the case name and the value is the associated data.
  - **Nullary Case:** Serializes to a string, e.g., `"Regular"`.
  - **Single-Field Case:** Serializes the field directly under the case name key, e.g., `"ElectricMotor": { ... }`.
  - **Multi-Field Case:** Serializes the fields as a JSON array, e.g., `"Hybrid": [ { ... }, { ... } ]`.
- **Easy to use:** A single `FSharpUnionConverterFactory` registration point handles all F# discriminated union types.

## Installation

Once the package is published to NuGet, you can install it using the .NET CLI:

```bash
dotnet add package FsDuJson
```

Usage
To use the converter, you need to add the FSharpUnionConverterFactory to your JsonSerializerOptions. A convenience module FSharpUnionJson is provided with FSharpUnionSerializerDefaultOptions() that does this for you.

Example:
```fsharp
open FsDuJson
open System.Text.Json

let myObj = MyObject.Default ()

// Use the default options
let options = FSharpUnionSerializerDefaultOptions()

// Serialize
let jsonString = JsonSerializer.Serialize(myObj, options)

// Deserialize
let deserialized = JsonSerializer.Deserialize<MyObject>(jsonString, options)
```

Using the following types that are heavy with discriminated unions, it will serialize to a much cleaner JSON format than other libraries using `Case` and `Fields` notation.

```json
{
  "Car": {
    "Make": "Toyota",
    "Model": "Prius",
    "Year": 2013,
    "DoorCount": 2,
    "PowerSource": {
      "Hybrid": [
        {
          "combustion": {
            "DisplacementCC": 1200.0,
            "Horsepower": 108,
            "TorqueLbFt": 105,
            "Fuel": "Hydrogen"
          }
        },
        {
          "BatteryKWh": 21.0,
          "Horsepower": 55,
          "TorqueLbFt": 60,
          "RangeMiles": 103
        },
        {
          "label": "Combination with generative braking"
        }
      ]
    }
  }
}
```

```fsharp
namespace FsDu

module VehicleTypes =

    type CombustionEngine = {
      DisplacementCC: decimal
      Horsepower: int
      TorqueLbFt: int
      Fuel: FuelType }

    and FuelType = 
      | Gasoline
      | Diesel
      | Hydrogen

    type ElectricMotor = {
      BatteryKWh: decimal
      Horsepower: int
      TorqueLbFt: int
      RangeMiles: int }

    type PowerSource =
      | CombustionEngine of CombustionEngine
      | ElectricMotor of ElectricMotor
      | Hybrid of combustion: CombustionEngine * ElectricMotor * label: string

    type Car = {
      Make: string
      Model: string
      Year: int
      DoorCount: int
      PowerSource: PowerSource }

    type Truck = {
      Make: string
      Model: string
      Year: int
      CabType: CabType
      BedLengthInches: int
      MaxTowPounds: int
      PowerSource: PowerSource }

    and CabType =
      | Regular
      | Extended
      | Crew

    type Motorcycle = {
      Make: string
      Model: string
      Year: int
      Category: MotorcycleCategory
      PowerSource: PowerSource }

    and MotorcycleCategory =
      | Cruiser
      | Sport
      | SuperSport
      | Touring
      | SportTouring
      | Adventure
      | Standard
      | DualSport
      | DirtBike

    type Vehicle =
      | Car of Car
      | Truck of Truck
      | Motorcycle of Motorcycle
```
