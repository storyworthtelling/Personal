namespace Constitution.Infrastructure.Delivery;

/// <summary>What one delivery attempt came to.</summary>
/// <param name="Name">The title the document goes under on the tablet.</param>
/// <param name="Delivered">True when the tablet's cloud took it.</param>
/// <param name="Failure">Why it did not, with the original exception kept; null when delivered.</param>
public sealed record DeliveryResult(string Name, bool Delivered, Exception? Failure);
