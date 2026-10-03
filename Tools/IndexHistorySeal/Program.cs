using IndexHistorySeal;

return SealCommand.Run(
    args,
    Environment.GetEnvironmentVariable,
    PassphrasePrompt.ReadWithoutEcho,
    Console.Out,
    Console.Error);
