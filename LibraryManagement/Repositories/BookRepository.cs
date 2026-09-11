using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;

namespace LibraryManagement.Repositories
{
    public class BookRepository
    {
        private readonly Database _db;

        public BookRepository()
        {
            _db = new Database();
        }

        public List<Book> GetAll()
        {
            var books = new List<Book>();
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = "SELECT BookId, Title, Author, Category, PublishYear, Quantity, AvailableQuantity FROM Books";
                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        books.Add(MapToBook(reader));
                }
            }
            return books;
        }

        public Book GetById(int id)
        {
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                return GetById(conn, null, id);
            }
        }

        // Overload dùng trong transaction (BorrowService Bước 4). Không tự mở/đóng connection.
        public Book GetById(SqlConnection conn, SqlTransaction tran, int id)
        {
            string sql = "SELECT BookId, Title, Author, Category, PublishYear, Quantity, AvailableQuantity FROM Books WHERE BookId = @BookId";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@BookId", id);
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                        return MapToBook(reader);
                }
            }
            return null;
        }

        // Trả về BookId vừa insert.
        public int Add(Book book)
        {
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                return Add(conn, null, book);
            }
        }

        public int Add(SqlConnection conn, SqlTransaction tran, Book book)
        {
            string sql = @"INSERT INTO Books (Title, Author, Category, PublishYear, Quantity, AvailableQuantity)
                           OUTPUT INSERTED.BookId
                           VALUES (@Title, @Author, @Category, @PublishYear, @Quantity, @AvailableQuantity)";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@Title", book.Title);
                cmd.Parameters.AddWithValue("@Author", book.Author);
                cmd.Parameters.AddWithValue("@Category", (object)book.Category ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PublishYear", book.PublishYear);
                cmd.Parameters.AddWithValue("@Quantity", book.Quantity);
                cmd.Parameters.AddWithValue("@AvailableQuantity", book.AvailableQuantity);
                return (int)cmd.ExecuteScalar();
            }
        }

        // Update toàn bộ field. KHÔNG dùng cho mượn/trả (dùng UpdateAvailableQuantity bên dưới).
        // Trả về false nếu BookId không tồn tại.
        public bool Update(Book book)
        {
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                return Update(conn, null, book);
            }
        }

        public bool Update(SqlConnection conn, SqlTransaction tran, Book book)
        {
            string sql = @"UPDATE Books SET
                               Title = @Title,
                               Author = @Author,
                               Category = @Category,
                               PublishYear = @PublishYear,
                               Quantity = @Quantity,
                               AvailableQuantity = @AvailableQuantity
                           WHERE BookId = @BookId";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@BookId", book.BookId);
                cmd.Parameters.AddWithValue("@Title", book.Title);
                cmd.Parameters.AddWithValue("@Author", book.Author);
                cmd.Parameters.AddWithValue("@Category", (object)book.Category ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PublishYear", book.PublishYear);
                cmd.Parameters.AddWithValue("@Quantity", book.Quantity);
                cmd.Parameters.AddWithValue("@AvailableQuantity", book.AvailableQuantity);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        // Tăng/giảm AvailableQuantity nguyên tử — dùng riêng cho BorrowBook()/ReturnBook() trong transaction.
        // delta = -1 khi mượn, +1 khi trả. Điều kiện ">= 0" trong WHERE là lớp chặn cuối chống race condition ở tầng DB.
        // Trả về false nếu update thất bại (BookId sai hoặc sẽ âm) -> Service phải rollback.
        public bool UpdateAvailableQuantity(SqlConnection conn, SqlTransaction tran, int bookId, int delta)
        {
            string sql = @"UPDATE Books SET AvailableQuantity = AvailableQuantity + @Delta
                           WHERE BookId = @BookId AND AvailableQuantity + @Delta >= 0";
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.AddWithValue("@BookId", bookId);
                cmd.Parameters.AddWithValue("@Delta", delta);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        // Chỉ xóa thẳng. Chặn xóa khi đang mượn -> BookService.DeleteBook() (Bước 4).
        // Trả về false nếu BookId không tồn tại.
        public bool Delete(int id)
        {
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = "DELETE FROM Books WHERE BookId = @BookId";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@BookId", id);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public List<Book> Search(string keyword)
        {
            var books = new List<Book>();
            using (var conn = _db.GetConnection())
            {
                conn.Open();
                string sql = @"SELECT BookId, Title, Author, Category, PublishYear, Quantity, AvailableQuantity
                               FROM Books
                               WHERE Title LIKE @Keyword OR Author LIKE @Keyword";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Keyword", $"%{keyword}%");
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            books.Add(MapToBook(reader));
                    }
                }
            }
            return books;
        }

        private Book MapToBook(SqlDataReader reader)
        {
            int categoryOrdinal = reader.GetOrdinal("Category");
            return new Book
            {
                BookId = reader.GetInt32(reader.GetOrdinal("BookId")),
                Title = reader.GetString(reader.GetOrdinal("Title")),
                Author = reader.GetString(reader.GetOrdinal("Author")),
                Category = reader.IsDBNull(categoryOrdinal) ? null : reader.GetString(categoryOrdinal),
                PublishYear = reader.GetInt32(reader.GetOrdinal("PublishYear")),
                Quantity = reader.GetInt32(reader.GetOrdinal("Quantity")),
                AvailableQuantity = reader.GetInt32(reader.GetOrdinal("AvailableQuantity"))
            };
        }
    }
}