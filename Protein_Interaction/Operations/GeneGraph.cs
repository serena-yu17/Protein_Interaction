using Protein_Interaction.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Protein_Interaction.Operations
{
    public class GeneGraph
    {
        private readonly DataContext _context;

        private static Dictionary<Tuple<int, int>, int> GeneRelation = null;
        private static Dictionary<string, int> geneid = null;
        private static Dictionary<int, string> idgene = null;
        private static Dictionary<int, List<int>> CacheUp = null;
        private static Dictionary<int, List<int>> CacheDown = null;


    }
}
