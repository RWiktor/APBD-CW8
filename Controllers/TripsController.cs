using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Tutorial8.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TripsController : ControllerBase
    {
        private readonly string _connectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=APBD;Integrated Security=True;";

        // Endpoint: GET /api/trips
        // Pobiera wszystkie wycieczki wraz z podstawowymi informacjami oraz listą krajów.
        [HttpGet]
        public async Task<IActionResult> GetTrips()
        {
            var trips = new List<TripDetailDTO>();

            string query = @"
                SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople,
                  -- Łączy nazwy krajów z wycieczki w pojedynczy ciąg znaków
                  STUFF((
                      SELECT ', ' + c.Name
                      FROM Country c
                      INNER JOIN Country_Trip ct ON c.IdCountry = ct.IdCountry
                      WHERE ct.IdTrip = t.IdTrip
                      FOR XML PATH(''), TYPE
                  ).value('.','NVARCHAR(MAX)'),1,2,'') AS Countries
                FROM Trip t;
            ";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                await conn.OpenAsync();

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        trips.Add(new TripDetailDTO
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("IdTrip")),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                            DateFrom = reader.GetDateTime(reader.GetOrdinal("DateFrom")),
                            DateTo = reader.GetDateTime(reader.GetOrdinal("DateTo")),
                            MaxPeople = reader.GetInt32(reader.GetOrdinal("MaxPeople")),
                            Countries = reader.IsDBNull(reader.GetOrdinal("Countries"))
                                ? new List<string>()
                                : new List<string>(reader.GetString(reader.GetOrdinal("Countries"))
                                    .Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                        });
                    }
                }
            }

            return Ok(trips);
        }
    }

    // DTO reprezentujący szczegóły wycieczki
    public class TripDetailDTO
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public int MaxPeople { get; set; }
        public List<string> Countries { get; set; }
    }
}