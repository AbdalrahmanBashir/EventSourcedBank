using EventSourcedBank.Api.Dtos;
using EventSourcedBank.Domain.BankAccount;
using EventSourcedBank.Domain.Exceptions;
using EventSourcedBank.Domain.ValueObjects;
using EventSourcedBank.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace EventSourcedBank.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountsController : ControllerBase
    {
        private readonly IBankAccountRepository _repo;

        public AccountsController(IBankAccountRepository repo) => _repo = repo;

        //Queries
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        {
            var agg = await _repo.Get(id, ct);
            if (agg is null) return NotFound();

            return Ok(ToDto(agg));
        }

        //Commands
        [HttpPost]
        [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Open([FromBody] OpenAccountRequest req, CancellationToken ct)
        {
            try
            {
                var id = Guid.NewGuid();
                var agg = BankAccountAggregate.Open(
                    id,
                    req.HolderName,
                    req.OverdraftLimit,
                    new Money(req.InitialBalance, req.Currency));

                await _repo.Save(agg, ct);
                return CreatedAtAction(nameof(GetById), new { id = agg.Id }, ToDto(agg));
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        }

        [HttpPost("{id:guid}/deposit")]
        [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Deposit(Guid id, [FromBody] MoneyChangeRequest req, CancellationToken ct)
        {
            try
            {
                var agg = await Load(id, ct);
                agg.Deposit(new Money(req.Amount, req.Currency));
                await _repo.Save(agg, ct);
                return Ok(ToDto(agg));
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (ConcurrencyException ex) { return Conflict(ex.Message); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        }

        [HttpPost("{id:guid}/withdraw")]
        [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Withdraw(Guid id, [FromBody] MoneyChangeRequest req, CancellationToken ct)
        {
            try
            {
                var agg = await Load(id, ct);
                agg.Withdraw(new Money(req.Amount, req.Currency));
                await _repo.Save(agg, ct);
                return Ok(ToDto(agg));
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (ConcurrencyException ex) { return Conflict(ex.Message); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        }

        [HttpPost("{id:guid}/freeze")]
        [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Freeze(Guid id, CancellationToken ct)
        {
            try
            {
                var agg = await Load(id, ct);
                agg.Freeze();
                await _repo.Save(agg, ct);
                return Ok(ToDto(agg));
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (ConcurrencyException ex) { return Conflict(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        }

        [HttpPost("{id:guid}/unfreeze")]
        [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Unfreeze(Guid id, CancellationToken ct)
        {
            try
            {
                var agg = await Load(id, ct);
                agg.Unfreeze();
                await _repo.Save(agg, ct);
                return Ok(ToDto(agg));
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (ConcurrencyException ex) { return Conflict(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        }

        [HttpPost("{id:guid}/close")]
        [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Close(Guid id, CancellationToken ct)
        {
            try
            {
                var agg = await Load(id, ct);
                agg.Close();
                await _repo.Save(agg, ct);
                return Ok(ToDto(agg));
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (ConcurrencyException ex) { return Conflict(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        }

        [HttpPatch("{id:guid}/overdraft-limit")]
        [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> ChangeOverdraftLimit(Guid id, [FromBody] ChangeOverdraftLimitRequest req, CancellationToken ct)
        {
            try
            {
                var agg = await Load(id, ct);
                agg.ChangeOverdraftLimit(req.NewLimit);
                await _repo.Save(agg, ct);
                return Ok(ToDto(agg));
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (ConcurrencyException ex) { return Conflict(ex.Message); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        }

        [HttpPatch("{id:guid}/holder-name")]
        [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> ChangeHolderName(Guid id, [FromBody] ChangeHolderNameRequest req, CancellationToken ct)
        {
            try
            {
                var agg = await Load(id, ct);
                agg.ChangeAccountHolderName(req.NewName);
                await _repo.Save(agg, ct);
                return Ok(ToDto(agg));
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (ConcurrencyException ex) { return Conflict(ex.Message); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        }

        [HttpPost("{id:guid}/fees")]
        [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> ApplyFee(Guid id, [FromBody] ApplyFeeRequest req, CancellationToken ct)
        {
            try
            {
                var agg = await Load(id, ct);
                agg.ApplyFee(new Money(req.Amount, req.Currency), req.Reason);
                await _repo.Save(agg, ct);
                return Ok(ToDto(agg));
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (ConcurrencyException ex) { return Conflict(ex.Message); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        }

        //Helpers
        private static AccountDto ToDto(BankAccountAggregate a) => new(
            a.Id,
            a.HolderName,
            a.Status.ToString(),
            a.Balance.Amount,
            a.Balance.Currency,
            a.OverdraftLimit,
            a.AvailableToWithdraw(),
            a.Version
        );

        private async Task<BankAccountAggregate> Load(Guid id, CancellationToken ct)
        {
            var agg = await _repo.Get(id, ct);
            if (agg is null) throw new KeyNotFoundException();
            return agg;
        }
    }
    
}
