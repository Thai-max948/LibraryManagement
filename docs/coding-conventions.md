# Clean code convention

Áp dụng cho mọi ngôn ngữ và mọi AI agent (Claude, Copilot, Cursor, Codex...). Đặt file này ở root repo với tên `AGENTS.md`, `CLAUDE.md` hoặc `.cursorrules`, hoặc dán vào system prompt.

## 0. Thứ tự ưu tiên

Khi các quy tắc mâu thuẫn, theo thứ tự:

1. Yêu cầu trực tiếp của người dùng trong phiên hiện tại.
2. Convention có sẵn trong codebase (đọc file lân cận trước khi viết).
3. Convention chuẩn của ngôn ngữ (C#: .NET guidelines, C++: Core Guidelines, Python: PEP 8, JS/TS: Airbnb/Google).
4. File này.

## 1. Nguyên tắc chung

- Code viết cho người đọc. Máy chạy được là điều kiện tối thiểu.
- Đúng trước, rõ thứ hai, nhanh thứ ba. Chỉ tối ưu khi có số đo.
- KISS: chọn cách đơn giản nhất giải quyết được vấn đề hiện tại.
- YAGNI: không viết tính năng "sau này có thể cần".
- DRY: lặp lại logic từ lần thứ 3 thì tách ra. Lặp 2 lần chưa cần.
- Một chỗ làm một việc (Single Responsibility).
- Ưu tiên composition hơn inheritance.
- Không magic number, không magic string. Đặt thành hằng số có tên.

## 2. Đặt tên

- Tên nói lên ý định: đọc tên là hiểu, không cần đọc thân hàm.
- Không viết tắt trừ từ phổ biến (`id`, `url`, `db`, `i` trong vòng lặp ngắn).
- Không đặt tên chung chung: `data`, `info`, `temp`, `obj`, `manager`, `helper`, `util`.
- Bool bắt đầu bằng `is`, `has`, `can`, `should`: `isActive`, `hasPermission`.
- Hàm là động từ: `getUser`, `calculateTotal`. Class là danh từ: `Invoice`, `OrderService`.
- Collection dùng số nhiều: `users`, `orderItems`.
- Không gắn kiểu vào tên (`userList`, `strName`) và không gắn tiền tố `I`/`m_` trừ khi convention ngôn ngữ yêu cầu (C# interface dùng `IFoo`).
- Một khái niệm dùng một từ trong toàn project (không lẫn `fetch`/`get`/`retrieve`).

Casing theo ngôn ngữ:

| Ngôn ngữ | Class/Type | Hàm/Method | Biến | Hằng |
|---|---|---|---|---|
| C# | PascalCase | PascalCase | camelCase | PascalCase |
| C++ | PascalCase | camelCase hoặc snake_case | snake_case | kPascalCase hoặc UPPER_SNAKE |
| Java/JS/TS | PascalCase | camelCase | camelCase | UPPER_SNAKE |
| Python | PascalCase | snake_case | snake_case | UPPER_SNAKE |

Trong C++ chọn một kiểu và giữ nhất quán với codebase.

## 3. Hàm

- Làm một việc. Nếu mô tả hàm cần chữ "và", tách ra.
- Ngắn: mục tiêu dưới 20 dòng, tối đa khoảng 40.
- Tối đa 3 tham số. Nhiều hơn thì gom thành object/struct.
- Không dùng bool flag làm tham số để đổi hành vi. Tách thành 2 hàm.
- Nhánh lồng tối đa 2 đến 3 cấp. Dùng guard clause và return sớm thay vì if lồng nhau.
- Tách query và command: hàm trả giá trị thì không đổi state, hàm đổi state thì không trả dữ liệu nghiệp vụ.
- Hạn chế side effect ẩn. Nếu có, đặt tên hàm cho thấy điều đó.
- Không trả `null` để biểu diễn "không có" khi có lựa chọn tốt hơn (`Optional`, `std::optional`, `T?`, empty collection).

## 4. Class và module

- Class nhỏ, một trách nhiệm. Trên khoảng 300 dòng là dấu hiệu cần tách.
- Field private mặc định. Chỉ mở public khi cần.
- Phụ thuộc đi qua constructor (dependency injection), không `new` cứng bên trong logic nghiệp vụ.
- Tách lớp: UI/Controller, Service/Logic, Repository/Data. Lớp trên gọi lớp dưới, không ngược lại.
- Không để logic nghiệp vụ trong code-behind, controller hoặc câu SQL nằm rải rác trong UI.
- Không circular dependency giữa module.

## 5. Comment

- Code tự giải thích bằng tên tốt. Comment giải thích "tại sao", không lặp lại "cái gì".
- Được comment: lý do chọn giải pháp lạ, workaround cho bug ngoài, ràng buộc nghiệp vụ, độ phức tạp thuật toán, TODO có ngữ cảnh.
- Không comment code chết. Xoá đi, Git đã lưu lịch sử.
- Không comment kiểu nhật ký (`// fixed by A on 12/3`).
- API public dùng doc comment (`///` C#, `/** */` Java/JS/C++) gồm mục đích, tham số, giá trị trả về, ngoại lệ.
- TODO ghi rõ việc và lý do: `// TODO: bỏ khi API v2 ra, xem #123`.
- Comment sai còn tệ hơn không có. Đổi code thì đổi comment.

## 6. Xử lý lỗi

- Fail fast: kiểm tra đầu vào ở ranh giới hàm, báo lỗi sớm.
- Dùng exception (hoặc `Result`/error code theo idiom ngôn ngữ) cho tình huống bất thường, không dùng cho luồng điều khiển bình thường.
- Không nuốt lỗi: cấm `catch {}` rỗng, cấm `except: pass`.
- Bắt exception cụ thể, không bắt `Exception` chung trừ ở tầng ngoài cùng (top-level handler).
- Thông báo lỗi có ngữ cảnh: điều gì sai, giá trị nào, ở đâu.
- Giải phóng tài nguyên chắc chắn: `using` (C#), RAII/smart pointer (C++), `with` (Python), `try-with-resources` (Java).
- Không hiển thị stack trace hoặc chi tiết nội bộ cho người dùng cuối. Ghi log riêng.

## 7. Định dạng

- Theo formatter của project (`dotnet format`, `clang-format`, `prettier`, `black`). Không tự chế style.
- Chưa có formatter: indent 4 space (C#, C++, Python, Java) hoặc 2 space (JS/TS), không dùng tab lẫn space.
- Độ dài dòng tối đa 100 đến 120 ký tự.
- Một câu lệnh một dòng. Một khai báo biến một dòng.
- Dùng dấu ngoặc `{}` cho mọi `if`/`for`/`while`, kể cả thân một dòng.
- Khai báo biến gần chỗ dùng đầu tiên, phạm vi hẹp nhất.
- Dòng trống để tách nhóm logic. Không dùng quá 1 dòng trống liên tiếp.
- Thứ tự trong file: import/using, hằng, field, constructor, public method, private method.
- Xoá import/using không dùng.

## 8. Kiểm thử

- Code mới có logic nghiệp vụ thì kèm test. Bug fix thì thêm test tái hiện bug trước.
- Tên test nêu hành vi: `Withdraw_InsufficientBalance_ThrowsException`.
- Cấu trúc Arrange, Act, Assert. Mỗi test kiểm một hành vi.
- Test độc lập, không phụ thuộc thứ tự chạy hay dữ liệu chung.
- Không gọi mạng, đọc giờ hệ thống hoặc random trực tiếp trong test. Mock hoặc inject.
- Test phải nhanh và ổn định. Test flaky thì sửa hoặc xoá.

## 9. Bảo mật và dữ liệu

- Không hard-code mật khẩu, API key, connection string. Dùng biến môi trường hoặc secret manager.
- Không commit file `.env`, key, dump dữ liệu thật.
- Query database luôn parameterized. Cấm nối chuỗi vào SQL.
- Kiểm tra và làm sạch mọi input từ ngoài (user, file, API).
- Không log dữ liệu nhạy cảm (mật khẩu, token, số thẻ).
- Mật khẩu lưu dạng hash có salt (bcrypt, Argon2, PBKDF2). Không tự viết crypto.
- Dùng quyền tối thiểu cần thiết cho mọi tài khoản và service.

## 10. Hiệu năng

- Đo trước khi tối ưu (profiler, benchmark).
- Chọn cấu trúc dữ liệu và thuật toán đúng trước, vi tối ưu sau.
- Tránh truy vấn N+1, lặp gọi I/O trong vòng lặp.
- Không tính lại giá trị bất biến bên trong vòng lặp.
- C++: truyền `const&` cho object lớn, dùng `reserve`, tránh copy thừa. C#: cẩn thận allocation trong đường nóng và LINQ lặp lại trên IEnumerable chưa materialize.

## 11. Quy tắc riêng cho AI agent

Khi sửa code có sẵn:

- Đọc file liên quan và file lân cận trước khi viết. Bắt chước convention đang dùng.
- Thay đổi nhỏ nhất đủ giải quyết yêu cầu. Không refactor, đổi tên, format lại phần không liên quan.
- Không xoá hoặc sửa test cho pass. Test fail thì tìm nguyên nhân.
- Không thêm dependency mới khi thư viện chuẩn hoặc dependency hiện có làm được. Cần thêm thì nêu lý do.
- Không đổi public API, schema database, format file cấu hình khi chưa được yêu cầu. Nếu buộc phải đổi, báo rõ.

Khi viết code mới:

- Nếu yêu cầu mơ hồ ở chỗ ảnh hưởng thiết kế, hỏi một câu ngắn. Còn lại thì nêu giả định và làm tiếp.
- Không bịa tên hàm, class, package hoặc API. Không chắc thì kiểm tra tài liệu hoặc nói rõ không chắc.
- Code phải chạy được và biên dịch được. Không để placeholder kiểu `// ... phần còn lại ...`, `throw new NotImplementedException()` trừ khi được yêu cầu skeleton.
- Không để `console.log`/`printf` debug, code bị comment, biến không dùng.
- Xử lý biên: rỗng, null, âm, tràn số, một phần tử, trùng lặp.

Khi báo cáo kết quả:

- Nêu ngắn gọn: đã đổi gì, ở file nào, vì sao.
- Nêu rõ phần chưa chạy thử hoặc chưa chắc chắn.
- Không khẳng định "đã test" nếu chưa chạy test.

## 12. Ví dụ

Xấu (C#):

```csharp
public decimal Calc(List<Item> l, bool f)
{
    decimal t = 0;
    if (l != null)
    {
        foreach (var i in l)
        {
            if (i.Q > 0)
            {
                if (f) t += i.P * i.Q * 0.9m;
                else t += i.P * i.Q;
            }
        }
    }
    return t;
}
```

Tốt:

```csharp
private const decimal MemberDiscountRate = 0.9m;

public decimal CalculateTotal(IEnumerable<OrderItem> items, bool isMember)
{
    ArgumentNullException.ThrowIfNull(items);

    var subtotal = items
        .Where(item => item.Quantity > 0)
        .Sum(item => item.Price * item.Quantity);

    return isMember ? subtotal * MemberDiscountRate : subtotal;
}
```

Xấu (C++):

```cpp
int f(vector<int> v, int x) {
    for (int i = 0; i < v.size(); i++)
        if (v[i] == x) return i;
    return -1;
}
```

Tốt:

```cpp
std::optional<size_t> findIndex(const std::vector<int>& values, int target) {
    for (size_t i = 0; i < values.size(); ++i) {
        if (values[i] == target) {
            return i;
        }
    }
    return std::nullopt;
}
```

## 13. Checklist trước khi giao code

- [ ] Biên dịch và chạy được, test hiện có vẫn pass.
- [ ] Tên rõ nghĩa, không viết tắt khó hiểu.
- [ ] Hàm ngắn, một việc, không lồng quá 3 cấp.
- [ ] Không magic number, không code lặp đáng tách.
- [ ] Xử lý lỗi và trường hợp biên, không nuốt exception.
- [ ] Không secret, không SQL nối chuỗi, không log dữ liệu nhạy cảm.
- [ ] Không code chết, không import thừa, không debug print.
- [ ] Chỉ đổi những gì được yêu cầu.
- [ ] Đã báo rõ phần chưa kiểm chứng.
