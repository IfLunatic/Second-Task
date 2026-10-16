using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Лабораторна робота: Багатопотоковий алгоритм обчислення суми масиву
/// методом хвильової редукції (wave reduction).
///
/// Алгоритм:
///   1. Маємо масив з N елементів.
///   2. На кожній хвилі: array[i] = array[i] + array[j], де j = n - 1 - i.
///   3. Якщо n непарне — середній елемент лишається без змін.
///   4. Після хвилі актуальна довжина стає ceil(n / 2).
///   5. Повторюємо, поки довжина не стане 1. array[0] — сума.
///
/// Мова: C#
/// Тип даних: long (64-бітне знакове ціле).
/// </summary>
class ParallelArraySum
{
    // ========================================================================
    // Послідовна (однопотокова) версія алгоритму хвильової редукції
    // ========================================================================

    /// <summary>
    /// Обчислює суму масиву послідовно за алгоритмом хвильової редукції.
    /// Масив модифікується на місці (in-place).
    /// </summary>
    /// <param name="array">Масив елементів</param>
    /// <returns>Сума всіх елементів</returns>
    static long SequentialWaveSum(long[] array)
    {
        // Актуальна довжина масиву — спочатку це повна довжина
        int n = array.Length;

        // Якщо масив порожній — сума 0
        if (n == 0) return 0;

        // Хвильовий цикл: працюємо, поки актуальна довжина > 1
        while (n > 1)
        {
            // Кількість пар на цій хвилі
            int pairs = n / 2;

            // Обробляємо кожну пару
            for (int i = 0; i < pairs; i++)
            {
                int j = n - 1 - i;              // Симетричний індекс
                array[i] = array[i] + array[j];  // Додаємо й записуємо в менший індекс
            }
            // Якщо n непарне, середній елемент (індекс n/2) лишається без змін —
            // ми його не чіпаємо, він автоматично потрапляє в нову активну частину.

            // Нова актуальна довжина: ceil(n / 2) = (n + 1) / 2
            n = (n + 1) / 2;
        }

        // Після всіх хвиль array[0] містить суму
        return array[0];
    }

    // ========================================================================
    // Паралельна (багатопотокова) версія алгоритму хвильової редукції
    // ========================================================================

    /// <summary>
    /// Обчислює суму масиву паралельно за алгоритмом хвильової редукції.
    ///
    /// Використовує масив Task для паралельного виконання.
    /// У .NET Task — це абстракція над пулом потоків (ThreadPool).
    /// Коли ми створюємо Task.Run(), середовище .NET бере вільний потік
    /// із вбудованого ThreadPool і виконує завдання в ньому.
    ///
    /// Різниця між Thread, Task і ThreadPool:
    ///   - Thread — системний потік ОС. Створення і знищення коштує дорого
    ///     (~1 МБ стеку, виклики ядра ОС).
    ///   - ThreadPool — пул попередньо створених потоків, які повторно
    ///     використовуються. Зменшує накладні витрати.
    ///   - Task — високорівнева абстракція. Task.Run() бере потік з ThreadPool,
    ///     виконує код і повертає потік назад. Ми працюємо з логічними
    ///     завданнями, не з потоками безпосередньо.
    ///
    /// Синхронізація: Task.WaitAll() блокує керуючий потік, поки всі
    /// Task у масиві не завершаться. Це аналог CountDownLatch у Java.
    /// </summary>
    /// <param name="array">Масив елементів (модифікується на місці)</param>
    /// <param name="numWorkers">Кількість паралельних завдань</param>
    /// <returns>Сума всіх елементів</returns>
    static long ParallelWaveSum(long[] array, int numWorkers)
    {
        int n = array.Length;

        // Якщо масив порожній або з одного елемента — паралелізм зайвий
        if (n == 0) return 0;
        if (n == 1) return array[0];

        // Хвильовий цикл
        while (n > 1)
        {
            // Кількість пар для обробки на поточній хвилі
            int pairs = n / 2;

            // Не створюємо більше завдань, ніж є пар
            int numTasks = Math.Min(numWorkers, pairs);

            // --- Розподіл пар між завданнями (чанками) ---
            // Кожне завдання отримує приблизно однакову кількість пар.
            int baseChunkSize = pairs / numTasks;
            int remainder = pairs % numTasks;

            // Зберігаємо актуальну довжину для замикання (closure)
            // У C# лямбда захоплює змінну за посиланням, тому потрібно
            // створити локальну копію, щоб уникнути перегонів даних.
            int currentN = n;

            // Масив завдань для поточної хвилі
            Task[] tasks = new Task[numTasks];
            int currentStart = 0;

            for (int t = 0; t < numTasks; t++)
            {
                // Розмір поточного чанка: базовий + 1, якщо є залишок
                int chunkSize = baseChunkSize + (t < remainder ? 1 : 0);

                // Локальні копії для замикання (closure capture)
                // У C# лямбда захоплює ЗМІННУ, а не ЗНАЧЕННЯ.
                // Якщо не створити локальну копію, всі лямбди отримають
                // останнє значення циклу — це типова помилка!
                int startIdx = currentStart;
                int endIdx = currentStart + chunkSize;
                int capturedN = currentN;

                // --- Створення завдання ---
                // Task.Run() бере потік із ThreadPool і виконує лямбду.
                // ThreadPool автоматично керує потоками: створює нові
                // при потребі, повторно використовує існуючі.
                tasks[t] = Task.Run(() =>
                {
                    // Робочий потік обробляє свій діапазон пар [startIdx, endIdx)
                    for (int i = startIdx; i < endIdx; i++)
                    {
                        // Обчислюємо симетричний індекс
                        int j = capturedN - 1 - i;
                        // Додаємо й записуємо результат
                        // Блокування не потрібне: кожна пара (i, j) обробляється
                        // одним потоком, і індекси i та j не перетинаються
                        // між різними завданнями.
                        array[i] = array[i] + array[j];
                    }
                });

                currentStart = endIdx;
            }

            // --- Очікування завершення всіх завдань поточної хвилі ---
            // Task.WaitAll() блокує поточний потік, поки всі Task не завершаться.
            // Це гарантує: наступна хвиля почнеться лише після повного
            // завершення поточної. Аналог CountDownLatch.await() у Java.
            Task.WaitAll(tasks);

            // Нова актуальна довжина
            n = (n + 1) / 2;
        }

        return array[0];
    }

    // ========================================================================
    // Ініціалізація масиву
    // ========================================================================

    /// <summary>
    /// Створює масив зі значеннями від 1 до size.
    /// Використовує long для уникнення переповнення.
    /// </summary>
    static long[] CreateArray(int size)
    {
        long[] array = new long[size];
        for (int i = 0; i < size; i++)
        {
            array[i] = (long)(i + 1); // Елементи: 1, 2, 3, ..., size
        }
        return array;
    }

    /// <summary>
    /// Створює копію масиву (алгоритм модифікує масив на місці).
    /// </summary>
    static long[] CopyArray(long[] source)
    {
        long[] copy = new long[source.Length];
        Array.Copy(source, copy, source.Length);
        return copy;
    }

    // ========================================================================
    // Тестування на малих масивах
    // ========================================================================

    /// <summary>
    /// Виконує тест на малому масиві та виводить результат.
    /// </summary>
    static void RunSmallTest(string name, long[] data, long expected)
    {
        // Копії масивів для послідовної та паралельної версій
        long[] seqArr = CopyArray(data);
        long seqResult = SequentialWaveSum(seqArr);

        long[] parArr = CopyArray(data);
        long parResult = ParallelWaveSum(parArr, Environment.ProcessorCount);

        bool seqOk = (seqResult == expected);
        bool parOk = (parResult == expected);
        bool match = (seqResult == parResult);

        Console.WriteLine($"  {name,-25} | Очік: {expected,4} | Послід: {seqResult,4} [{(seqOk ? "OK" : "ПОМИЛКА")}] | Парал: {parResult,4} [{(parOk ? "OK" : "ПОМИЛКА")}] | Збіг: {(match ? "Так" : "НІ")}");
    }

    // ========================================================================
    // Головний метод — точка входу
    // ========================================================================

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("=============================================================");
        Console.WriteLine("  Лабораторна робота: Багатопотоковий алгоритм суми масиву");
        Console.WriteLine("  Мова: C# | Алгоритм: хвильова редукція");
        Console.WriteLine("=============================================================");
        Console.WriteLine();

        // --- Інформація про систему ---
        int processorCount = Environment.ProcessorCount;
        Console.WriteLine($"Логічних процесорів: {processorCount}");
        Console.WriteLine($"64-бітний процес: {Environment.Is64BitProcess}");
        Console.WriteLine($"Версія .NET: {Environment.Version}");
        Console.WriteLine();

        // ================================================================
        // 1. Тестування на малих масивах
        // ================================================================
        Console.WriteLine("--- Тестування на малих масивах ---");

        // Парна кількість елементів
        RunSmallTest("[1,2,3,4,5,6]", new long[] { 1, 2, 3, 4, 5, 6 }, 21);
        // Непарна кількість елементів
        RunSmallTest("[1,2,3,4,5]", new long[] { 1, 2, 3, 4, 5 }, 15);
        // Один елемент
        RunSmallTest("[7]", new long[] { 7 }, 7);
        // Нульові значення
        RunSmallTest("[0,0,0]", new long[] { 0, 0, 0 }, 0);
        // Від'ємні значення
        RunSmallTest("[-1,2,-3,4]", new long[] { -1, 2, -3, 4 }, 2);
        // Два елементи
        RunSmallTest("[10,20]", new long[] { 10, 20 }, 30);
        // Вісім елементів
        RunSmallTest("[1..8]", new long[] { 1, 2, 3, 4, 5, 6, 7, 8 }, 36);

        Console.WriteLine();

        // ================================================================
        // 2. Великий тест
        // ================================================================

        // Цільовий розмір: 1 мільярд елементів
        // Пам'ять: 1_000_000_000 × 8 байт = ~7.45 ГБ
        int targetN = 1_000_000_000;
        int actualN;

        // Перевірка доступної пам'яті через GC
        // GC.GetGCMemoryInfo().TotalAvailableMemoryBytes дає загальну пам'ять системи
        long totalMemory;
        try
        {
            totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
        catch
        {
            // Якщо не вдалося отримати — оцінюємо консервативно
            totalMemory = 4L * 1024 * 1024 * 1024; // 4 ГБ за замовчуванням
        }

        long requiredBytes = (long)targetN * 8L;
        // Залишаємо ~2 ГБ для ОС та .NET Runtime
        long availableBytes = totalMemory - 2L * 1024 * 1024 * 1024;

        if (Environment.Is64BitProcess && availableBytes >= requiredBytes * 2)
        {
            // Потрібно два масиви (оригінал + копія для тестів)
            actualN = targetN;
            Console.WriteLine($"Великий тест: N = {actualN:N0} (повний розмір)");
        }
        else
        {
            // Використовуємо менший розмір
            actualN = 100_000_000; // 100 мільйонів
            Console.WriteLine($"УВАГА: Недостатньо пам'яті або 32-бітний процес.");
            Console.WriteLine($"       Потрібно: {requiredBytes / (1024.0 * 1024.0 * 1024.0):F2} ГБ");
            Console.WriteLine($"       Доступно: {availableBytes / (1024.0 * 1024.0 * 1024.0):F2} ГБ");
            Console.WriteLine($"       Використовуємо N = {actualN:N0}");
        }
        Console.WriteLine();

        // Очікувана сума: S = N * (N + 1) / 2
        // Обчислення з long для уникнення переповнення
        long expectedSum = (long)actualN * ((long)actualN + 1L) / 2L;
        Console.WriteLine($"Очікувана сума (формула): {expectedSum:N0}");
        Console.WriteLine();

        // --- Ініціалізація масиву ---
        // Stopwatch — високоточний таймер у .NET.
        // Він використовує апаратний лічильник продуктивності (Performance Counter).
        // На відміну від DateTime.Now, Stopwatch монотонний і не залежить
        // від синхронізації годинника.
        Stopwatch sw = new Stopwatch();

        Console.Write("Ініціалізація масиву... ");
        sw.Start();
        long[] baseArray = CreateArray(actualN);
        sw.Stop();
        Console.WriteLine($"Готово за {sw.Elapsed.TotalSeconds:F3} сек");
        Console.WriteLine();

        // ================================================================
        // 3. Послідовна версія
        // ================================================================
        Console.WriteLine("--- Послідовна версія ---");
        {
            long[] arr = CopyArray(baseArray);
            sw.Restart();
            long result = SequentialWaveSum(arr);
            sw.Stop();

            Console.WriteLine($"  Результат:  {result:N0}");
            Console.WriteLine($"  Коректно:   {(result == expectedSum ? "Так" : "НІ")}");
            Console.WriteLine($"  Час:        {sw.Elapsed.TotalSeconds:F3} сек ({sw.ElapsedMilliseconds} мс)");
            Console.WriteLine();
        }

        // ================================================================
        // 4. Паралельна версія з різною кількістю потоків
        // ================================================================
        Console.WriteLine("--- Паралельна версія (бенчмарки) ---");
        Console.WriteLine($"  {"Потоків",-12} | {"Час",-20} | {"Результат",-18} | Коректно");
        Console.WriteLine("  " + new string('-', 70));

        // Набір кількостей потоків
        var threadCounts = new HashSet<int> { 1, 2, 4, 8, processorCount };

        foreach (int numWorkers in threadCounts.OrderBy(x => x))
        {
            // Свіжа копія масиву для кожного запуску
            long[] arr = CopyArray(baseArray);

            // Вимірюємо лише час обчислення
            sw.Restart();
            long result = ParallelWaveSum(arr, numWorkers);
            sw.Stop();

            bool correct = (result == expectedSum);
            Console.WriteLine($"  {numWorkers,-12} | {sw.Elapsed.TotalSeconds,8:F3} сек ({sw.ElapsedMilliseconds,5} мс) | {result,18:N0} | {(correct ? "Так" : "ПОМИЛКА")}");
        }

        Console.WriteLine();
        Console.WriteLine("=============================================================");
        Console.WriteLine("  Завершено.");
        Console.WriteLine("=============================================================");
    }
}

/*
 * =======================================================================
 * ПОЯСНЕННЯ КОДУ БЛОК ЗА БЛОКОМ
 * =======================================================================
 *
 * 1. ПРОСТОРИ ІМЕН (using)
 *    - System: базові типи (Console, Math, Array, Environment, GC).
 *    - System.Diagnostics: Stopwatch для вимірювання часу.
 *    - System.Threading: Thread, ThreadPool, синхронізація.
 *    - System.Threading.Tasks: Task, Task.Run(), Task.WaitAll().
 *    - System.Collections.Generic: HashSet для унікальних кількостей потоків.
 *    - System.Linq: OrderBy для сортування.
 *
 * 2. ПОСЛІДОВНА ВЕРСІЯ (SequentialWaveSum)
 *    - Ідентична логіці Java-версії.
 *    - while(n > 1) — цикл хвиль.
 *    - for(i = 0; i < pairs; i++) — обробка пар.
 *    - j = n - 1 - i — симетричний індекс.
 *    - (n + 1) / 2 — ceil(n/2) через цілочисельне ділення.
 *
 * 3. ПАРАЛЕЛЬНА ВЕРСІЯ (ParallelWaveSum)
 *    3a. Task vs Thread vs ThreadPool
 *        - Thread — системний потік ОС. Дорогий у створенні (~1 МБ стеку).
 *        - ThreadPool — пул попередньо створених потоків .NET Runtime.
 *          Потоки повторно використовуються. Ми не керуємо ними напряму.
 *        - Task — логічна одиниця роботи. Task.Run() ставить лямбду
 *          в чергу ThreadPool. Потік із пулу забирає й виконує її.
 *        - Task — це "обіцянка" (promise), що роботу буде виконано.
 *          Task.WaitAll() чекає виконання всіх обіцянок.
 *
 *    3b. Task.WaitAll()
 *        - Блокує поточний потік, поки всі Task не завершаться.
 *        - Гарантує: наступна хвиля починається лише після повного
 *          завершення поточної.
 *        - Аналог CountDownLatch.await() у Java.
 *
 *    3c. Замикання (closures) та захоплення змінних
 *        - У C# лямбда захоплює ЗМІННУ, а не її поточне ЗНАЧЕННЯ.
 *        - Приклад помилки:
 *            for (int t = 0; t < 10; t++)
 *                Task.Run(() => Console.Write(t)); // ПОМИЛКА: може вивести 10 десять разів!
 *        - Правильно:
 *            int localT = t;
 *            Task.Run(() => Console.Write(localT)); // OK: кожна лямбда має свою копію
 *        - Тому ми створюємо startIdx, endIdx, capturedN як локальні змінні.
 *
 *    3d. Чому не потрібні блокування (lock)?
 *        - Кожна пара (i, j) обробляється рівно одним завданням.
 *        - i < n/2, j >= n/2 → індекси не перетинаються.
 *        - array[j] лише читається, array[i] лише пишеться одним потоком.
 *        - Немає спільного запису → немає перегонів даних.
 *
 * 4. ІНІЦІАЛІЗАЦІЯ МАСИВУ
 *    - Елементи від 1 до N, тип long (8 байт кожний).
 *    - Для N = 10^9: 8 × 10^9 байт ≈ 7.45 ГБ.
 *    - Потрібен 64-бітний процес із достатньою пам'яттю.
 *
 * 5. ВИМІРЮВАННЯ ЧАСУ (Stopwatch)
 *    - Stopwatch.StartNew() або new + Start() — запуск таймера.
 *    - Stop() — зупинка.
 *    - Restart() — скидання та повторний запуск.
 *    - ElapsedMilliseconds, Elapsed.TotalSeconds — результат.
 *    - Використовує QueryPerformanceCounter (Windows) або clock_gettime (Linux).
 *    - Точність: мікросекунди або навіть наносекунди.
 *
 * 6. ПЕРЕВІРКА ПАМ'ЯТІ
 *    - GC.GetGCMemoryInfo() дає інформацію про доступну пам'ять.
 *    - Якщо її недостатньо — зменшуємо N до 100 мільйонів.
 *    - Для повного тесту потрібен 64-бітний процес.
 *
 * 7. ЩО СТАНЕТЬСЯ, ЯКЩО...
 *    - ...не використати Task.WaitAll()?
 *      Наступна хвиля почнеться до завершення поточної → перегони даних.
 *    - ...не створити локальні копії для замикання?
 *      Усі лямбди отримають останнє значення циклу → хибний результат.
 *    - ...використати int замість long?
 *      Переповнення при великих сумах (> 2^31 - 1 = 2,147,483,647).
 *    - ...створити Task для кожної пари окремо?
 *      Мільйони Task → накладні витрати на планування перевищать
 *      корисну роботу. Чанки значно ефективніші.
 */
