using System;
using System.IO;
using System.Linq;

namespace CbUploader
{
    public record ClientBillingFile(FileInfo File, FileInfo OutputFile, int Attempts) { }
    public record FailedOutputFile(FileInfo File, DirectoryInfo OutputParentDirectory, string ErrorMessage) { }
    public enum StatementType { NcBranch, NcAgency, NmAgency, Srcn, Mbdot, DirectOps }
}
