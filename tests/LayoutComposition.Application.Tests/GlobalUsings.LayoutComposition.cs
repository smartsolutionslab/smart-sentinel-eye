// Two spec 258 US1 fakes/tests under Fakes/ and EventHandlers/ reference
// FabIdentifier and OperatorIdentifier without importing the namespaces
// those types live in. Global usings here give them the same names every
// other Wall test file imports directly, without touching a file this PR
// may not edit (CLAUDE.md's Phase 4b rule).
global using SmartSentinelEye.LayoutComposition.Domain.Layout;
global using SmartSentinelEye.Shared.Kernel;
