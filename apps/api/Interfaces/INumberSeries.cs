namespace Erp.Api.Interfaces;

using Erp.Api;

// Task-2 §2: doc numbering over PG sequences (one per prefix) — ids keep the SUP-0007 format.
// P5 (016): admin view + void bookkeeping — expose the existing service, never rebuild it.
public interface INumberSeries
{
    string Next(string prefix);
    List<NumberSeriesView> List();
    bool Exists(string prefix);
    bool AlreadyVoided(string prefix, string number);
    void AddVoid(NumberSeriesVoid v);
}
