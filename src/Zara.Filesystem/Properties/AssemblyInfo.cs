using System.Runtime.CompilerServices;

// PathSyntax is internal: it's an implementation detail of PathCanonicalizer
// and PathValidator, not part of this assembly's public surface. The test
// project needs direct access to it because it IS the security-critical logic
// the adversarial suite (§27.1) targets — testing it only through the higher-
// level PathValidator would make failures harder to localize.
[assembly: InternalsVisibleTo("Zara.Filesystem.Tests")]
