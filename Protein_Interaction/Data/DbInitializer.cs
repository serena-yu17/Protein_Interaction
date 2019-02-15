using Microsoft.Extensions.FileProviders;
using Protein_Interaction.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Protein_Interaction.Data
{
    public static class DbInitializer
    {
        public static void Initialize(DataContext context)
        {
            //context.Database.EnsureCreated();             
        }
    }
}
