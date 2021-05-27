## unreleased 

## Changed

- bump cecilifier version to 1.12.0
- switched to net5.0
- fixed error reporting on discord channel
- fixed volatile field handling
- fixed generated code for delegate initialization / calling
- fixed a couple of ref parameter issues
- do not quote (') zipped project file.
- improved support for generics in general
- updated Microsoft.CodeAnalysis.CSharp to version 3.10.0-3.final
- updated Mono.Cecil to version 0.11.3
- updated Newtonsoft.Json to version 13.0.1
- added support for *out variables*
- added support for *ref return*
- added support for *local ref*
- added code coverage
- changed release notes UI.

## Added

- basic support for Range/Index
- support for switch statements
- set assembly entry point with Main() method from first class one is defined
- array/indexers support

## 04/July/2020

## Changed

- better comments on each generated code section
- report errors on discord channel

## Added

- a lot of other stuff that has not been reported since March/2019 :(
- Support for for statements	

## 24/March/2019

## Changed

- fixed type resolution crashing in some scenarios
- wrong opcode being generated for some types

## Added

- Support for instantiating single dimensional arrays 

## 24/March/2019

### Changed

- *call* instruction emitted with invalid target in some scenarios
- fixed calls to overloaded methods/ctors always being resolved to the first member processed.
- take operands of various instructions (eg, call/calli/callvirt) into account when comparing assemblies.

### Added

- Support access of generic static/instance methods.