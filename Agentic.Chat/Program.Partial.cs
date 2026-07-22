// Behavior-neutral visibility lift so WebApplicationFactory<Program> (which lives in
// a separate test assembly) can target the implicitly-generated Program type that
// backs Program.cs top-level statements. The compiler synthesizes `internal partial
// class Program` for top-level statements; merging this empty partial widens it to
// public without adding any members, fields, or code paths.
public partial class Program { }
