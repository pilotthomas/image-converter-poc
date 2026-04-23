using System.Collections.Generic;
using System.Xml.Serialization;

namespace ImageConverterPoc
{
    [XmlRoot("ArrayOfConversionTask20")]
    public class ConversionTask20Document
    {
        [XmlElement("ConversionTask20")]
        public List<ConversionTask20> Tasks { get; set; } = new List<ConversionTask20>();
    }

    public class ConversionTask20
    {
        public bool CanDeleteSourceFile { get; set; }

        public string SourcePath { get; set; }

        public string DestinationPath { get; set; }

        public string ArchivePath { get; set; }

        public bool CreateUniqueName { get; set; }

        public string ImgFormat { get; set; }

        public bool Decollate { get; set; }

        public bool AutoRotate { get; set; }

        /// <summary>Optional legacy field present in some task files.</summary>
        public string Error { get; set; }
    }
}
