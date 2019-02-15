using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Protein_Interaction.Models
{
    public class RefQuery
    {
        public int gene1 { get; set; }
        public int gene2 { get; set; }
        public int refID { get; set; }
        public string author { get; set; }

        public RefQuery(int gene1, int gene2, int refID, string author)
        {
            this.gene1 = gene1;
            this.gene2 = gene2;
            this.refID = refID;
            this.author = author;
        }
    }

    public class ReferenceModel
    {
        public int gene1 { get; set; }
        public string geneName1 { get; set; }
        public int gene2 { get; set; }
        public string geneName2 { get; set; }
        public List<PaperModel> references { get; set; }
    }
    public class PaperModel
    {
        public int refID { get; set; }
        public string author { get; set; }
    }
    public class GraphModel
    {
        public int status { get; set; }
        public float xmax { get; set; }
        public float ymax { get; set; }
        public List<string[]> vertex { get; set; }
        public List<int> query { get; set; }
        public List<int[]> edge { get; set; }  
        public string refKey { get; set; }

        public GraphModel()
        {
            vertex = new List<string[]>();
            query = new List<int>();
            edge = new List<int[]>();
        }
    }

    public class Reactome
    {
        [Key]
        public int Gene1 { get; set; }

        [Key]
        public int Gene2 { get; set; }

        [Required]
        public int Confidence { get; set; }
    }

    public class GeneId
    {
        [Key]
        public int ID { get; set; }

        [Required]
        public string Symbol { get; set; }
    }

    public class Refs
    {
        [Key]
        public int Gene1 { get; set; }

        [Key]
        public int Gene2 { get; set; }

        [Key]
        public int RefID { get; set; }

        [Required]
        public string Author { get; set; }

    }

    public class Synons
    {
        [Key]
        public string Symbol { get; set; }

        [Required]
        public int ID { get; set; }
    }


    public class Node
    {
        public Node next { get; set; }
        public int geneID { get; set; }
        public bool reverse { get; set; } = false;
        public Node(Node next, int symbol, bool reverse)
        {
            this.next = next;
            this.geneID = symbol;
            this.reverse = reverse;
        }
    }
}

