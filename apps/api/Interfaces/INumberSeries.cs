namespace Erp.Api.Interfaces;

// Task-2 §2: doc numbering over PG sequences (one per prefix) — ids keep the SUP-0007 format
public interface INumberSeries
{
    string Next(string prefix);
}
