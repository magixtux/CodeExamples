﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureService.Controllers
{
    public interface IController<T>
    {
        void Add(T entity);
        void Remove(T entity);
        T Get(int id);
        T Get(string q);
        IEnumerable<T> GetAll();
        IEnumerable<T> Find(string query);

    }
}
