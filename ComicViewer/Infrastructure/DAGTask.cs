namespace ComicViewer.Infrastructure
{
    public class DAGTask
    {
        internal TaskCompletionSource<bool> start = new();
        public List<string> requirements = new();
        public Func<Task> task = null!;
        public string name = null!;
    }

    public class DAGTaskManager
    {
        private Dictionary<string, Task> running_tasks = new();
        private List<DAGTask> pending_tasks = new();

        private static async Task TaskWrapper(DAGTask task)
        {
            await task.start.Task;
            await task.task();
        }
        private async Task RequirementWrapper(DAGTask task)
        {
            List<Task> reqs = new();
            foreach (var item in task.requirements)
            {
                if (running_tasks.TryGetValue(item, out var req))
                {
                    reqs.Add(req);
                }
            }
            await Task.WhenAll(reqs);
            task.start.TrySetResult(true);
        }
        public void Add(DAGTask task)
        {
            pending_tasks.Add(task);
        }
        public void Remove(string name)
        {
            pending_tasks.RemoveAll(e => e.name == name);
        }
        public void Run()
        {
            foreach (var task in pending_tasks)
            {
                running_tasks[task.name] = TaskWrapper(task);
            }
            foreach (var task in pending_tasks)
            {
                _ = RequirementWrapper(task);
            }
        }
        public async Task Done()
        {
            await Task.WhenAll(running_tasks.Values);
        }
    }
}
