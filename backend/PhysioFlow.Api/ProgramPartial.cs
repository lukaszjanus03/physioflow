// WebApplicationFactory<T> wymaga publicznej klasy Program.
// Top-level statements w Program.cs generują klasę Program w GLOBALNYM namespace jako internal.
// Ten partial bez namespace'u dokleja się do tej samej klasy i robi ją public.
public partial class Program { }