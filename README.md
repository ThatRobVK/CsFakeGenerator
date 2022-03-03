# C# Fake Generator
Generates fakes (classes with no implemenation) from a compiled C# DLL.

## What is a fake?
In this context, a fake is a class that has the same public interface as a real class, but no implementation. This means that all methods return the default value for their return type, properties simply have getters and setters and there is no functional code.

Effectively they're stubs, but actual code of stubs, rather than something generated at runtime by a mocking framework.

### What it does exactly
CsFakeGenerator loads a .NET assembly DLL, uses reflection to inspect all public types and their public members, generates C# code for the types and members, before finally saving each type as a `.cs` file in the output folder.

## But, why?
Take for example an open source project that relies on licensed third-party libraries. You are unable to freely distribute the licensed libraries, so you create fakes to distribute, allowing the code to compile and run. Contributors won't be able to use the licensed functionality unless they acquire their own licenses, but the code can be written in a way where it doesn't fail if the libraries are fakes instead of real.

## How to use

### Build
Open up the solution and compile it. It's only a single class and creates a console app.

### Command line
`.\CsFakeGenerator.exe "input file" "output folder"`

Input file is a .NET assembly DLL that you want to generate a fake for. Output path is a folder to save the cs files to. If your input file path or output folder path contain spaces, put the whole path in quotes.

### Assembly references
If your library has dependencies that aren't in the GAC, copy them to the same folder as the input file. The tool does not copy these files to the output folder.

### How to build the output
The easiest way is to create a new C# project in your IDE of choice, set it up to build however you want to build the output, and then run CsFakeGenerator with the output directory set to that project's folder. In my case, my IDE (Rider) imports the files automatically and I can build the project with the generated files. You will have to ensure you add any assembly references to the project manually.

Alternatively, you can also use `Csc.exe` to build the files in the output folder, again taking care to ensure references are taken care of.

## Code

### Code quality
OK I admit, there is none. I wrote this in an exploratory way as I was figuring it out as I went. It's all in one class (only a few hundred lines) and it doesn't have tests. I'm not even ashamed. OK, maybe a little.

If I continue working on this - which entirely depends on whether my requirements change or not - then I may clean it up a bit as I go. If you contribute, feel free to clean bits up.

### Known limitations
This is a utility I wrote for my own use, for some very specific libraries I wanted to generate fakes for. It does what I need it to do, but definitely doesn't covery everything that you might encounter in a library. For example attributes aren't covered at all. And there are certainly combinations for access modifiers and overrides etc that won't come through correctly.

This was tested against a few libraries I needed it for, but there isn't much in the way of exception handling so you may encounter situations where it blows up.

### Csproj generation
It's broken, basically. I tried to create a csproj programmatically but it didn't want to load dependencies. May revisit this, for now use the instructions above.

## License
This project is licensed under the MIT license. Feel free to use it for whatever.
