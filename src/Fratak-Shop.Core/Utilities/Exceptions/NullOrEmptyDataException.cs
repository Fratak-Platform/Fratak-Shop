namespace Fratak_Shop.Core.Utilities.Exceptions;

public class NullOrEmptyDataException(string message) : Exception(message)
{

}

public class DataRangeException(string message) : Exception(message)
{

}

public class InvalidCommandException(string? message) : Exception(message);