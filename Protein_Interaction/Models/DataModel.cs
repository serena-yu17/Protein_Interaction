using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Protein_Interaction.Models
{
    public class CountModel
    {
        public string id;
        public string syn;
        public string nref;
    }

    [ProtoContract]
    public class SingleQueryModel
    {
        [ProtoMember(1)]
        public string query { get; set; }
        [ProtoMember(2)]
        public uint updepth { get; set; }
        [ProtoMember(3)]
        public uint ddepth { get; set; }
        [ProtoMember(4)]
        public uint width { get; set; }

        public int instanceID { get; set; }
    }

    [ProtoContract]
    public class MultiQueryModel
    {
        [ProtoMember(1)]
        public int[] queries { get; set; }

        public int instanceID { get; set; }

        private static readonly char[] delim = new char[]
        {
            ',', ' ', ';'
        };

        public MultiQueryModel(string query, int instanceID)
        {
            this.instanceID = instanceID;

            var sections = query.Split(delim, StringSplitOptions.RemoveEmptyEntries);
            List<int> queryGeneID = new List<int>();
            foreach (var sec in sections)
                if (int.TryParse(sec, out var intVal))
                    queryGeneID.Add(intVal);
            queries = queryGeneID.ToArray();
        }
    }

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

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public struct GenePair
    {
        public int Gene1;
        public int Gene2;

        public GenePair(int gene1, int gene2)
        {
            Gene1 = gene1;
            Gene2 = gene2;
        }

        public override bool Equals(object other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (!(other is GenePair pair))
                return false;
            return Gene1 == pair.Gene1 && Gene2 == pair.Gene2;
        }

        public static bool operator ==(GenePair pair1, GenePair pair2)
        {
            return pair1.Equals(pair2);
        }

        public static bool operator !=(GenePair pair1, GenePair pair2)
        {
            return pair1.Equals(pair2);
        }

        public override int GetHashCode()
        {
            return (int)(((long)Gene1 + Gene2) * ((long)Gene1 + Gene2 + 1) / 2 + Gene2);
        }
    }

    public class SearchResultModel
    {
        public GraphModel graph;
        public Failure failure;
        public GenePair[] referencePairs;

        public SearchResultModel(GraphModel graph, Failure failure, GenePair[] references)
        {
            this.graph = graph;
            this.failure = failure;
            this.referencePairs = references;
        }
    }

    public class Failure
    {
        int status;

        public Failure(int status)
        {
            this.status = status;
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

