using Microsoft.EntityFrameworkCore;
using Protein_Interaction.Models;

namespace Protein_Interaction.Data
{
    public class DataContext : DbContext
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options)
        {
            
        }
        public DbSet<Reactome> Reactomes { get; set; }
        public DbSet<GeneId> GeneID { get; set; }
        public DbSet<Refs> Ref { get; set; }
        public DbSet<Synons> Synon { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Reactome>().HasKey(key => new { key.Gene1, key.Gene2 });
            modelBuilder.Entity<GeneId>().HasKey(key => key.ID);
            modelBuilder.Entity<Refs>().HasKey(key => new { key.Gene1, key.Gene2, key.RefID });
            modelBuilder.Entity<Synons>().HasKey(key => key.Symbol);
        }
    }
}
