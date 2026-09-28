using System.Runtime.CompilerServices;

// Lets Sense.Tests.Integration unit-test seed-generation internals (e.g. DataSeeder's demo employee routing/title
// helpers) directly, without standing up the full DataSeeder + DbContext graph for what is pure branching logic.
[assembly: InternalsVisibleTo("Sense.Tests.Integration")]
[assembly: InternalsVisibleTo("Sense.Tests.Unit")]
