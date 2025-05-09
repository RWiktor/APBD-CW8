using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using Microsoft.Data.SqlClient;

namespace Tutorial8.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClientsController : ControllerBase
    {
        private readonly string _connectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=APBD;Integrated Security=True;";

        // Endpoint: GET /api/clients/{id}/trips
        // Pobiera wycieczki, na które klient jest zarejestrowany, wraz z informacją o rejestracji.
        [HttpGet("{id}/trips")]
        public async Task<IActionResult> GetClientTrips(int id)
        {
            var trips = new List<ClientTripDTO>();

            // Sprawdzenie czy klient istnieje
            string clientQuery = "SELECT COUNT(1) FROM Client WHERE IdClient = @IdClient";
            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(clientQuery, conn))
            {
                cmd.Parameters.AddWithValue("@IdClient", id);
                await conn.OpenAsync();
                var exists = (int)await cmd.ExecuteScalarAsync();
                if (exists == 0)
                    return NotFound("Client not found");
                conn.Close();
            }

            string query = @"
                SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople, ct.RegisteredAt
                FROM Trip t
                INNER JOIN Client_Trip ct ON t.IdTrip = ct.IdTrip
                WHERE ct.IdClient = @IdClient;
            ";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@IdClient", id);
                await conn.OpenAsync();

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        trips.Add(new ClientTripDTO
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("IdTrip")),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                            DateFrom = reader.GetDateTime(reader.GetOrdinal("DateFrom")),
                            DateTo = reader.GetDateTime(reader.GetOrdinal("DateTo")),
                            MaxPeople = reader.GetInt32(reader.GetOrdinal("MaxPeople")),
                            RegisteredAt = reader.GetDateTime(reader.GetOrdinal("RegisteredAt"))
                        });
                    }
                }
            }

            if (trips.Count == 0)
                return NotFound("No trips found for this client");

            return Ok(trips);
        }

        // Endpoint: POST /api/clients
        // Tworzy nowy rekord klienta w bazie danych z walidacją danych wejściowych.
        [HttpPost]
        public async Task<IActionResult> CreateClient([FromBody] ClientDTO client)
        {
            if (string.IsNullOrWhiteSpace(client.FirstName) ||
                string.IsNullOrWhiteSpace(client.LastName) ||
                string.IsNullOrWhiteSpace(client.Email) ||
                string.IsNullOrWhiteSpace(client.Telephone) ||
                string.IsNullOrWhiteSpace(client.Pesel))
            {
                return BadRequest("All fields are required");
            }

            int newId = 0;
            string query = @"
                INSERT INTO Client (FirstName, LastName, Email, Telephone, Pesel)
                VALUES (@FirstName, @LastName, @Email, @Telephone, @Pesel);
                SELECT CAST(SCOPE_IDENTITY() AS int);
            ";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@FirstName", client.FirstName);
                cmd.Parameters.AddWithValue("@LastName", client.LastName);
                cmd.Parameters.AddWithValue("@Email", client.Email);
                cmd.Parameters.AddWithValue("@Telephone", client.Telephone);
                cmd.Parameters.AddWithValue("@Pesel", client.Pesel);
                await conn.OpenAsync();

                newId = (int)await cmd.ExecuteScalarAsync();
            }

            return CreatedAtAction(nameof(GetClientTrips), new { id = newId }, new { clientId = newId });
        }

        // Endpoint: PUT /api/clients/{id}/trips/{tripId}
        // Rejestruje klienta na daną wycieczkę ze sprawdzeniem limitu uczestników.
        [HttpPut("{id}/trips/{tripId}")]
        public async Task<IActionResult> RegisterClientToTrip(int id, int tripId)
        {
            // Zapytania do walidacji i wstawienia rejestracji
            string clientQuery = "SELECT COUNT(1) FROM Client WHERE IdClient = @IdClient";
            string tripQuery = "SELECT MaxPeople FROM Trip WHERE IdTrip = @IdTrip";
            string countQuery = "SELECT COUNT(1) FROM Client_Trip WHERE IdTrip = @IdTrip";
            string insertQuery = "INSERT INTO Client_Trip (IdClient, IdTrip, RegisteredAt) VALUES (@IdClient, @IdTrip, @RegisteredAt)";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // Sprawdzenie istnienia klienta
                using (SqlCommand cmd = new SqlCommand(clientQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@IdClient", id);
                    var clientExists = (int)await cmd.ExecuteScalarAsync();
                    if (clientExists == 0)
                        return NotFound("Client not found");
                }

                // Sprawdzenie istnienia wycieczki oraz pobranie limitu uczestników
                int maxPeople;
                using (SqlCommand cmd = new SqlCommand(tripQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@IdTrip", tripId);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result == null)
                        return NotFound("Trip not found");
                    maxPeople = (int)result;
                }

                // Sprawdzenie bieżącej liczby rejestracji
                int currentRegistrations;
                using (SqlCommand cmd = new SqlCommand(countQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@IdTrip", tripId);
                    currentRegistrations = (int)await cmd.ExecuteScalarAsync();
                }

                if (currentRegistrations >= maxPeople)
                {
                    return BadRequest("Max number of participants reached");
                }

                // Wstawienie nowej rejestracji klienta na wycieczkę
                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@IdClient", id);
                    cmd.Parameters.AddWithValue("@IdTrip", tripId);
                    cmd.Parameters.AddWithValue("@RegisteredAt", DateTime.Now);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            return Ok("Client registered to trip successfully");
        }

        // Endpoint: DELETE /api/clients/{id}/trips/{tripId}
        // Usuwa rejestrację klienta z wycieczki.
        [HttpDelete("{id}/trips/{tripId}")]
        public async Task<IActionResult> UnregisterClientFromTrip(int id, int tripId)
        {
            string checkQuery = "SELECT COUNT(1) FROM Client_Trip WHERE IdClient = @IdClient AND IdTrip = @IdTrip";
            string deleteQuery = "DELETE FROM Client_Trip WHERE IdClient = @IdClient AND IdTrip = @IdTrip";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                int registrationCount;
                using (SqlCommand cmd = new SqlCommand(checkQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@IdClient", id);
                    cmd.Parameters.AddWithValue("@IdTrip", tripId);
                    registrationCount = (int)await cmd.ExecuteScalarAsync();
                }

                if (registrationCount == 0)
                    return NotFound("Registration not found");

                using (SqlCommand cmd = new SqlCommand(deleteQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@IdClient", id);
                    cmd.Parameters.AddWithValue("@IdTrip", tripId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            return Ok("Client unregistered from trip successfully");
        }
    }

    // DTO dla klienta przy tworzeniu nowego rekordu
    public class ClientDTO
    {
        public string FirstName { get; set; }
        public string LastName  { get; set; }
        public string Email     { get; set; }
        public string Telephone { get; set; }
        public string Pesel     { get; set; }
    }

    // DTO reprezentujący wycieczkę klienta wraz z datą rejestracji
    public class ClientTripDTO
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public int MaxPeople { get; set; }
        public DateTime RegisteredAt { get; set; }
    }
}